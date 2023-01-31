using BoundfoxStudios.CommunityProject.Infrastructure;
using UnityEngine;

namespace BoundfoxStudios.CommunityProject.Buildings.Towers
{
	[SelectionBase]
	public class Tower : MonoBehaviour
	{
		[field: SerializeField]
		public Collider Collider { get; private set; } = default!;

		private void OnValidate()
		{
			Guard.AgainstNull(() => Collider, this);
		}
	}
}
