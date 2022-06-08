using BepInEx;
using HarmonyLib;
using RoR2;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Permissions;
using UnityEngine;
using UnityEngine.Networking;
using Console = System.Console;

[assembly: AssemblyVersion(Local.Eclipse.CurseCatcher.Plugin.versionNumber)]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
		// Allow private member access via publicized assemblies.

namespace Local.Eclipse.CurseCatcher
{
	[BepInPlugin("local.eclipse.cursecatcher", "CurseCatcher", versionNumber)]
	public class Plugin : BaseUnityPlugin
	{
		public const string versionNumber = "0.1.2";

		private static bool enableArtifact, enableLog,
				disableInteractable, disableSelfDamage, disableFriendlyFire, disableFallDamage;

		public void Awake()
		{
			string sectionTitle = "General";

			disableSelfDamage = Config.Bind(
					section: sectionTitle,
					key: "Self Damage",
					defaultValue: false,
					description: "Character abilities that cost health to activate will curse "
						+ "the user if this parameter is set to true."
				).Value is false;

			disableFriendlyFire = Config.Bind(
					section: sectionTitle,
					key: "Friendly Fire",
					defaultValue: true,
					description: "Effects that damage allies may apply curse when enabled."
				).Value is false;

			disableFallDamage = Config.Bind(
					section: sectionTitle,
					key: "Fall Damage",
					defaultValue: true,
					description: "Inflict curse upon taking sufficient fall damage."
				).Value is false;

			disableInteractable = Config.Bind(
					section: sectionTitle,
					key: "Interactable Cost",
					defaultValue: true,
					description: "Enable curse when spending health on interactables "
						+ "(e.g. Shrine of Blood & Void Cradles)."
				).Value is false;

			sectionTitle = "Other";

			enableArtifact = Config.Bind(
					section: sectionTitle,
					key: "Artifact Enabled",
					defaultValue: false,
					description: "Optional artifact to toggle the above functionality. " 
						+ "Can be selected in the Eclipse lobby or used to enable curse debuff "
						+ "in other game modes. "
				).Value;

			enableLog = Config.Bind(
					section: sectionTitle,
					key: "Detailed Logging",
					defaultValue: false,
					description: "For troubleshooting purposes."
				).Value;

			if ( enableArtifact ) Harmony.CreateAndPatchAll(typeof(Artifact));
			Harmony instance = null;

			Run.onRunStartGlobal += ( _ ) => {
					if ( instance is null && NetworkServer.active )
						instance = Harmony.CreateAndPatchAll(typeof(Plugin));
				};

			Run.onRunDestroyGlobal += ( _ ) => {
					instance?.UnpatchSelf();
					instance = null;
				};
		}

		[HarmonyPatch(typeof(HealthComponent), nameof(HealthComponent.TakeDamage))]
		[HarmonyTranspiler]
		private static IEnumerable<CodeInstruction> InsertCodeInstruction(
				IEnumerable<CodeInstruction> instructionList)
		{
			MethodInfo adjustDifficulty = typeof(Plugin).GetMethod(
					nameof(Plugin.AdjustDifficulty));
			MethodInfo getDifficulty = typeof(Run).GetProperty(
					nameof(Run.selectedDifficulty)
				).GetMethod;

			foreach ( CodeInstruction instruction in instructionList )
			{
				yield return instruction;

				if ( instruction.Calls(getDifficulty) )
				{
					yield return new CodeInstruction(OpCodes.Ldarg_1);
					yield return new CodeInstruction(OpCodes.Call, adjustDifficulty);
				}
			}
		}

		public static DifficultyIndex AdjustDifficulty(
				DifficultyIndex currentDifficulty, DamageInfo damageInfo)
		{
			bool disableCurse = false;
			if ( enableLog ) PrintInfo(damageInfo);

			if ( damageInfo.attacker is null )
			{
				if ( damageInfo.damageType.HasFlag(DamageType.FallDamage) )
					disableCurse = disableFallDamage;
				else switch ( damageInfo.damageType )
				{
					case DamageType.NonLethal:
					case DamageType.NonLethal | DamageType.BypassArmor:
						disableCurse = disableSelfDamage;
						break;
				}
			}
			else if ( damageInfo.attacker != null )		// Operator overloaded by Unity.
			{
				TeamComponent team = damageInfo.attacker.GetComponent<TeamComponent>();
				if ( team != null && team.teamIndex == TeamIndex.Player )
					disableCurse = disableFriendlyFire;
				else if ( damageInfo.attacker.GetComponent<IInteractable>() is object )
					disableCurse = disableInteractable;
			}

			if ( disableCurse && Artifact.Enabled != false )
			{
				if ( enableLog ) Console.WriteLine("...preventing curse.");
				return DifficultyIndex.Invalid;
			}
			
			if ( Artifact.Enabled == true )
				return DifficultyIndex.Eclipse8;

			return currentDifficulty;
		}

		private static void PrintInfo(DamageInfo damageInfo)
		{
			string attacker = getName(damageInfo.attacker),
					inflictor = getName(damageInfo.inflictor);

			string getName(GameObject obj)
			{
				string name;
				if ( obj is null ) name = "null";
				else if ( obj == null ) name = "destroyed";
				else name = obj.name;

				int start = name.IndexOf('('), count = name.IndexOf(')') - start + 1;
				if ( start >= 0 && count >= 0 )
					name = name.Remove(start, count);

				return name;
			}

			string info = "attacker: " + attacker;
			if ( inflictor != attacker && damageInfo.inflictor is object )
				info += " (" + inflictor + ")";

			if ( damageInfo.attacker != null )
			{
				TeamComponent team = damageInfo.attacker.GetComponent<TeamComponent>();
				if ( team != null )
					info += ", team: " + team.teamIndex;
			}

			info += ", type: " + damageInfo.damageType;
			Console.WriteLine(info);
		}
	}
}
