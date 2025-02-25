﻿using UnityEngine;

namespace Items.Cargo.Wrapping
{
	public class WrappedItem: WrappedBase, ICheckedInteractable<HandApply>, ICheckedInteractable<InventoryApply>, IServerSpawn
	{
		[SerializeField]
		[Tooltip("Use this to set the initial type of this object. " +
		         "The sprite will change to represent this change when the object is spawned. " +
		         "Useful for mapping!")]
		private PackageType packageType;

		private Pickupable pickupable;
		private ItemAttributesV2 itemAttributesV2;



		protected override void OnEnable()
		{
			base.OnEnable();
			pickupable = GetComponent<Pickupable>();
			itemAttributesV2 = GetComponent<ItemAttributesV2>();
		}

		public override void UnWrap()
		{
			PlayUnwrappingSound();
			var unwrapped = GetOrGenerateContent();
			if (unwrapped == null) return;
			MakeContentVisible(unwrapped);
			RetrieveObject(unwrapped,gameObject.AssumedWorldPosServer());

			if (pickupable.ItemSlot == null)
			{
				_ = Despawn.ServerSingle(gameObject);
			}
			else
			{
				Inventory.ServerAdd(
					unwrapped,
					pickupable.ItemSlot,
					ReplacementStrategy.DespawnOther);
			}
		}

		public void SetSprite(PackageType type)
		{
			spriteHandler.ChangeSprite((int) type);
		}

		public void SetSize(Size size)
		{
			itemAttributesV2.ServerSetSize(size);
		}

		#region Click in the world
		public bool WillInteract(HandApply interaction, NetworkSide side)
		{
			return DefaultWillInteract.Default(interaction, side) &&
			       interaction.HandObject == null &&
			       interaction.Intent == Intent.Harm;
		}

		public void ServerPerformInteraction(HandApply interaction)
		{
			StartUnwrapAction(interaction.Performer);
		}
		#endregion

		#region Click in inventory
		public bool WillInteract(InventoryApply interaction, NetworkSide side)
		{
			return DefaultWillInteract.Default(interaction, side) &&
			       interaction.UsedObject == null &&
			       interaction.Intent == Intent.Harm;
		}

		public void ServerPerformInteraction(InventoryApply interaction)
		{
			StartUnwrapAction(interaction.Performer);
		}
		#endregion

		public override void OnSpawnServer(SpawnInfo info)
		{
			base.OnSpawnServer(info);

			if (info.SpawnType != SpawnType.Mapped) return;

			SetSprite(packageType);
		}
	}

	public enum PackageType
	{
		Box,
		Tiny,
		Small,
		Medium,
		Large,
		Sphere
	}
}
