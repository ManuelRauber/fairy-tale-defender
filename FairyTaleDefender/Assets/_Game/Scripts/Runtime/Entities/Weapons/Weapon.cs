using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using BoundfoxStudios.FairyTaleDefender.Entities.Buildings.Towers;
using BoundfoxStudios.FairyTaleDefender.Entities.Weapons.ScriptableObjects;
using BoundfoxStudios.FairyTaleDefender.Entities.Weapons.Targeting;
using BoundfoxStudios.FairyTaleDefender.Entities.Weapons.Targeting.ScriptableObjects;
using BoundfoxStudios.FairyTaleDefender.Extensions;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BoundfoxStudios.FairyTaleDefender.Entities.Weapons
{
	[SelectionBase]
	public abstract class Weapon<TWeaponSO, TTargetLocatorSO, TEffectiveWeaponDefinition> : MonoBehaviour,
		ICanCalculateEffectiveWeaponDefinition
		where TWeaponSO : WeaponSO
		where TTargetLocatorSO : TargetLocatorSO<TEffectiveWeaponDefinition>
		where TEffectiveWeaponDefinition : EffectiveWeaponDefinition
	{
		[field: SerializeField]
		public TWeaponSO WeaponDefinition { get; private set; } = default!;

		[SerializeField]
		protected TTargetLocatorSO TargetLocator = default!;

		[SerializeField]
		protected EffectiveWeaponCalculatorSO<TWeaponSO, TEffectiveWeaponDefinition> EffectiveWeaponCalculatorSO = default!;

		[field: SerializeField]
		public Tower Tower { get; private set; } = default!;

		[field: SerializeField]
		public TargetTypeSO TargetType { get; private set; } = default!;

		private TEffectiveWeaponDefinition EffectiveWeaponDefinition =>
			_effectiveWeaponDefinition ??=
				(TEffectiveWeaponDefinition)CalculateEffectiveWeaponDefinition(Tower.transform.position);

		/// <summary>
		/// Launches the actual projectile to the target.
		/// </summary>
		/// <returns></returns>
		protected abstract UniTask LaunchProjectileAsync(Vector3 target, CancellationToken cancellationToken);

		/// <summary>
		/// Starts the weapon launch animation.
		/// </summary>
		protected abstract UniTask StartAnimationAsync(CancellationToken cancellationToken);

		/// <summary>
		/// Animation that should be played after launching the projectile.
		/// </summary>
		protected abstract UniTask RewindAnimationAsync(CancellationToken cancellationToken);

		/// <summary>
		/// Use this method to track a target, e.g. rotate the weapon towards the target.
		/// </summary>
		protected abstract void TrackTarget(TargetPoint target);

		private TEffectiveWeaponDefinition? _effectiveWeaponDefinition;
		private TargetPoint? _currentTarget;
		private Vector3 _towerForward;

		protected void Start()
		{
			_towerForward = Tower.transform.forward;

			StartLaunchSequenceAsync().Forget();
		}

		public EffectiveWeaponDefinition CalculateEffectiveWeaponDefinition(Vector3 position)
		{
			_effectiveWeaponDefinition = EffectiveWeaponCalculatorSO.Calculate(WeaponDefinition, position);
			return _effectiveWeaponDefinition;
		}

		protected virtual void Update()
		{
			if (!_currentTarget && !TryAcquireTarget(out _currentTarget))
			{
				return;
			}

			var isTargetInRangeAndAlive = IsTargetInRangeAndAlive(_currentTarget);

			if (!isTargetInRangeAndAlive)
			{
				_currentTarget = null;
				return;
			}

			TrackTarget(_currentTarget!);
		}

		private bool IsTargetInRangeAndAlive(TargetPoint? target) =>
			target.Exists() && TargetLocator.IsInAttackRange(transform.position, target.transform.position, _towerForward,
				EffectiveWeaponDefinition);

		private bool TryAcquireTarget([NotNullWhen(true)] out TargetPoint? currentTarget)
		{
			currentTarget = TargetLocator.Locate(transform.position, _towerForward, TargetType, EffectiveWeaponDefinition);
			return currentTarget;
		}

		/// <summary>
		/// This is the main sequence to launch a projectile to a target.
		/// </summary>
		private async UniTaskVoid StartLaunchSequenceAsync()
		{
			var token = destroyCancellationToken;

			// First get the launch delay (fire rate) to wait before we can launch a projectile
			var launchDelay = CalculateLaunchDelay();
			await UniTask.Delay(launchDelay, cancellationToken: token);

			// If we don't have a target, we wait until we have a target, so we can launch a projectile immediately.
			if (!_currentTarget)
			{
				await UniTask.WaitUntil(() => _currentTarget, cancellationToken: token);
			}

			var targetPosition = _currentTarget!.transform.position;

			// Note: It could be that between start animation and launch projectile the current target is destroyed.
			// In that case we still launch the projectile to the last known position.
			await ExecuteIfNotCancelledAsync(token, StartAnimationAsync);
			await ExecuteIfNotCancelledAsync(token, async t => await LaunchProjectileAsync(targetPosition, t));
			await ExecuteIfNotCancelledAsync(token, RewindAnimationAsync);

			if (!token.IsCancellationRequested)
			{
				// Recursive call of this function to start another launch sequence.
				StartLaunchSequenceAsync().Forget();
			}
		}

		private async UniTask ExecuteIfNotCancelledAsync(CancellationToken token, Func<CancellationToken, UniTask> action)
		{
			if (token.IsCancellationRequested)
			{
				return;
			}

			await action(token);
		}

		private TimeSpan CalculateLaunchDelay()
		{
			var delay = WeaponDefinition.FireRateEverySeconds - CalculateLaunchAnimationDelay();

			Debug.Assert(delay > 0,
				$"{nameof(CalculateLaunchDelay)} returns delay {delay} that is smaller or equal than 0, that is not valid!");

			return TimeSpan.FromSeconds(delay);
		}

		protected virtual float CalculateLaunchAnimationDelay() => 0;
	}
}
