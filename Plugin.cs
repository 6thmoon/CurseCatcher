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
		public const string versionNumber = "0.2.0";

		private static bool enableArtifact, enableLog,
				selfDamage, friendlyFire, fallDamage, interactableCost;

		public void Awake()
		{
			string sectionTitle = "General";

			selfDamage = Config.Bind(
					section: sectionTitle,
					key: "Self Damage",
					defaultValue: false,
					description: "Character abilities that cost health to activate will curse "
						+ "the user if this parameter is set to true."
				).Value;

			friendlyFire = Config.Bind(
					section: sectionTitle,
					key: "Friendly Fire",
					defaultValue: true,
					description: "Effects that damage allies may apply curse when enabled."
				).Value;

			fallDamage = Config.Bind(
					section: sectionTitle,
					key: "Fall Damage",
					defaultValue: true,
					description: "Inflict curse upon taking sufficient fall damage."
				).Value;

			interactableCost = Config.Bind(
					section: sectionTitle,
					key: "Interactable Cost",
					defaultValue: true,
					description: "Enable curse when spending health on interactables "
						+ "(e.g. Shrine of Blood & Void Cradles)."
				).Value;

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
					if ( instance is null && NetworkServer.active && Artifact.Enabled != false )
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
			MethodInfo getSelectedDifficulty = typeof(Run).GetProperty(
					nameof(Run.selectedDifficulty)).GetMethod;

			bool found = false;
			CodeInstruction previousInstruction = null;

			if ( enableLog ) Console.Write("Installing hook for 'Eclipse 8' curse...");
			foreach ( CodeInstruction instruction in instructionList )
			{
				if ( found && instruction.Branches(out _) )
				{
					found = false;

					if ( previousInstruction.LoadsConstant(DifficultyIndex.Eclipse8) )
					{
						if ( enableLog ) Console.Write("...instruction sequence found.");

						yield return new CodeInstruction(OpCodes.Pop);
						yield return new CodeInstruction(OpCodes.Ldarg_1);

						yield return CodeInstruction.Call(
								typeof(Plugin), nameof(Plugin.ApplyCurse));

						yield return new CodeInstruction(OpCodes.Brfalse, instruction.operand);
						continue;
					}
				}
				else if ( instruction.Calls(getSelectedDifficulty) )
					found = true;

				previousInstruction = instruction;
				yield return instruction;
			}
		}

		public static bool ApplyCurse(DifficultyIndex difficulty, DamageInfo damageInfo)
		{
			if ( difficulty < DifficultyIndex.Eclipse8 && Artifact.Enabled != true )
				return false;

			bool applyCurse = true;
			if ( enableLog ) PrintInfo(damageInfo);

			if ( damageInfo.attacker is null )
			{
				if ( damageInfo.damageType.HasFlag(DamageType.FallDamage) )
					applyCurse &= fallDamage;
				else switch ( damageInfo.damageType )
				{
					case DamageType.NonLethal:
					case DamageType.NonLethal | DamageType.BypassArmor:
						applyCurse &= selfDamage;
						break;
				}
			}
			else if ( damageInfo.attacker != null )		// Operator overloaded by Unity.
			{
				TeamComponent team = damageInfo.attacker.GetComponent<TeamComponent>();
				if ( team != null && team.teamIndex == TeamIndex.Player )
					applyCurse &= friendlyFire;
				else if ( damageInfo.attacker.GetComponent<IInteractable>() is object )
					applyCurse &= interactableCost;
			}

			if ( applyCurse ) return true;
			else
			{
				if ( enableLog ) Console.WriteLine("...preventing curse.");
				return false;
			}
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
				else if ( obj.name is null ) name = "unknown";
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
