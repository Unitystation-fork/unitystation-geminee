using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Mirror;
using Communications;
using UI.Systems.Tooltips.HoverTooltips;

namespace Items.Weapons
{
	/// <summary>
	/// Interaction script for explosives that can go on walls, objects, players, etc.
	/// </summary>
	public class AttachableExplosive : ExplosiveBase,
		ICheckedInteractable<PositionalHandApply>, IRightClickable, ICheckedInteractable<HandApply>,
		 IInteractable<HandActivate>, IHoverTooltip
	{
		[SyncVar] private bool isOnObject = false;
		private GameObject attachedToObject;
		private IHoverTooltip hoverTooltipImplementation;

		private void OnDisable()
		{
			Emitter = null;
			UpdateManager.Remove(CallbackType.PERIODIC_UPDATE, UpdateBombPosition);
		}

		[Server]
		private void AttachExplosive(GameObject target, Vector2 targetPostion)
		{
			if (target.TryGetComponent<UniversalObjectPhysics>(out var handler))
			{
				Inventory.ServerDrop(pickupable.ItemSlot, targetPostion);
				attachedToObject = target;
				UpdateManager.Add(UpdateBombPosition, 0.1f);
				scaleSync.SetScale(new Vector3(0.6f, 0.6f, 0.6f));
				return;
			}

			Inventory.ServerDrop(pickupable.ItemSlot, targetPostion);
			//Visual feedback to indicate that it's been attached and not just dropped.
			scaleSync.SetScale(new Vector3(0.6f, 0.6f, 0.6f));
		}

		private void UpdateBombPosition()
		{
			if(attachedToObject == null) return;
			if(attachedToObject.AssumedWorldPosServer() == gameObject.AssumedWorldPosServer()) return;
			registerItem.ObjectPhysics.Component.AppearAtWorldPositionServer(attachedToObject.AssumedWorldPosServer());
		}

		[Command(requiresAuthority = false)]
		private void CmdTellServerToDeattachExplosive()
		{
			DeAttachExplosive();
		}

		[Server]
		private void DeAttachExplosive()
		{
			isOnObject = false;
			pickupable.ServerSetCanPickup(true);
			objectBehaviour.SetIsNotPushable(false);
			scaleSync.SetScale(new Vector3(1f, 1f, 1f));
			UpdateManager.Remove(CallbackType.PERIODIC_UPDATE, UpdateBombPosition);
			attachedToObject = null;
		}

		/// <summary>
		/// checks to see if we can attach the explosive to an object.
		/// </summary>
		private bool CanAttatchToTarget(Matrix matrix, RegisterTile tile)
		{
			return matrix.Get<RegisterDoor>(tile.WorldPositionServer, true).Any() || matrix.Get<Pickupable>(tile.WorldPositionServer, true).Any();
		}


		public bool WillInteract(PositionalHandApply interaction, NetworkSide side)
		{
			if (DefaultWillInteract.Default(interaction, side) == false || pickupable.ItemSlot == null) return false;

			// (???): Why do we prevent mounting if it is armed?
			// (Max): Because accessing the UI and other elements of this explosive gets disabled when an explosive is armed.
			// If players try to do anything after it's armed they're going to break something because that's not intended behavior.
			if (isArmed)
			{
				Chat.AddExamineMsg(interaction.Performer, $"The {gameObject.ExpensiveName()} is already armed!", side);
				return false;
			}

			// (???): Why?
			// (Max): Have you tried looking at the video I posted below in the comments?
			// These conditions are there to avoid players shooting themselves in the foot.
			if (interaction.Intent != Intent.Harm)
			{
				Chat.AddExamineMsg(interaction.Performer, $"You must be on harm intent to attach the {gameObject.ExpensiveName()}.", side);
				return false;
			}

			return true;
		}

		public void ServerPerformInteraction(PositionalHandApply interaction)
		{
			void Perform()
			{
				if (interaction.TargetObject?.OrNull().RegisterTile()?.OrNull().Matrix?.OrNull() != null)
				{
					var matrix = interaction.TargetObject.RegisterTile().Matrix;
					var tiles = matrix.GetRegisterTile(interaction.TargetObject.TileLocalPosition().To3Int(), true);
					//Check to see if we're trying to attach the object to things we're not supposed to
					//because we don't want stuff like this happening :
					//https://youtu.be/0Yu8hEBMRwc
					foreach (var registerTile in tiles)
					{
						if (CanAttatchToTarget(registerTile.Matrix, registerTile) == false) continue;
						Chat.AddExamineMsg(interaction.Performer,
							$"The {interaction.TargetObject.ExpensiveName()} isn't a good spot to arm the explosive on..");
						return;
					}
				}

				AttachExplosive(interaction.TargetObject, interaction.TargetVector);
				isOnObject = true;
				pickupable.ServerSetCanPickup(false);
				objectBehaviour.SetIsNotPushable(true);
				SoundManager.PlayNetworkedAtPos(beepSound, gameObject.AssumedWorldPosServer());
				Chat.AddActionMsgToChat(interaction.Performer,
					$"You attach the {gameObject.ExpensiveName()} to {interaction.TargetObject.ExpensiveName()}",
					$"{interaction.PerformerPlayerScript.visibleName} attaches a {gameObject.ExpensiveName()} to {interaction.TargetObject.ExpensiveName()}!");
			}

			//The progress bar that triggers Preform()
			//Must not be interrupted for it to work.
			var bar = StandardProgressAction.Create(
				new StandardProgressActionConfig(StandardProgressActionType.CPR, false, false), Perform);
			bar.ServerStartProgress(interaction.Performer.RegisterTile(), progressTime, interaction.Performer);
		}

		public bool WillInteract(InventoryApply interaction, NetworkSide side)
		{
			return interaction.TargetSlot.IsEmpty;
		}

		public RightClickableResult GenerateRightClickOptions()
		{
			RightClickableResult result = new RightClickableResult();
			if (isOnObject == false) return result;
			if (CustomNetworkManager.IsServer)
			{
				result.AddElement("Deattach", DeAttachExplosive);
			}
			else
			{
				result.AddElement("Deattach", CmdTellServerToDeattachExplosive);
			}
			return result;
		}

		public bool WillInteract(HandApply interaction, NetworkSide side)
		{
			if (DefaultWillInteract.Default(interaction, side) == false) return false;
			if (isOnObject && interaction.HandObject == null) return true;
			if (isOnObject == false || interaction.HandObject != null &&
			    interaction.HandObject.TryGetComponent<SignalEmitter>(out var emitter) == false) return false;
			return true;
		}

		public void ServerPerformInteraction(HandApply interaction)
		{
			if(HackEmitter(interaction)) return;
			explosiveGUI.ServerPerformInteraction(interaction);
		}

		public void ServerPerformInteraction(HandActivate interaction)
		{
			explosiveGUI.ServerPerformInteraction(interaction);
		}

		public string HoverTip()
		{
			return isArmed == false ? null : "It appears to be armed!";
		}

		public string CustomTitle()
		{
			return null;
		}

		public Sprite CustomIcon()
		{
			return null;
		}

		public List<Sprite> IconIndicators()
		{
			return null;
		}

		public List<TextColor> InteractionsStrings()
		{
			if (isArmed) return null;
			TextColor armText = new TextColor
			{
				Text = "Place the bomb on an object to arm it while in harm intent.",
				Color = Color.red
			};

			List<TextColor> interactions = new List<TextColor>();
			interactions.Add(armText);
			return interactions;
		}
	}
}
