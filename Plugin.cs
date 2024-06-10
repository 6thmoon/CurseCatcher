using BepInEx;
using BepInEx.Configuration;
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
		public const string versionNumber = "0.3.0";

		private static bool enableArtifact;
		private static ConfigEntry<bool>
				selfDamage, friendlyFire, fallDamage, interactableCost, stageHazard, enableLog;

		public void Awake()
		{
			const string general = "General", other = "Other";

			selfDamage = Config.Bind(
					section: general,
					key: "Self Damage",
					defaultValue: false,
					description: "Abilities that cost health to activate will curse the player "
						+ "if this setting is enabled.");

			friendlyFire = Config.Bind(
					section: general,
					key: "Friendly Fire",
					defaultValue: true,
					description: "Effects that damage allies may apply curse when enabled.");

			fallDamage = Config.Bind(
					section: general,
					key: "Fall Damage",
					defaultValue: true,
					description: "Inflict curse upon taking sufficient fall damage.");

			interactableCost = Config.Bind(
					section: general,
					key: "Interactable Cost",
					defaultValue: true,
					description: "Whether the debuff is triggered upon paying health costs.");

			stageHazard = Config.Bind(
					section: general,
					key: "Neutral Hazards",
					defaultValue: true,
					description: "If neutral damage, such as stage hazards, can cause curse.");

			enableArtifact = Config.Bind(
					section: other,
					key: "Artifact Enabled",
					defaultValue: false,
					description: "Used to toggle the above functionality. Selectable in the "
						+ "Eclipse lobby, but also applies the modifier to other game modes."
				).Value;

			enableLog = Config.Bind(
					section: other,
					key: "Detailed Logging",
					defaultValue: false,
					description: "For troubleshooting purposes only.");

			if ( enableArtifact ) Harmony.CreateAndPatchAll(typeof(Artifact));
			else Harmony.CreateAndPatchAll(typeof(Plugin));
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

			Console.WriteLine("Installing hook for 'Eclipse 8' curse...");
			foreach ( CodeInstruction instruction in instructionList )
			{
				if ( found && instruction.Branches(out _) )
				{
					found = false;

					if ( previousInstruction.LoadsConstant(DifficultyIndex.Eclipse8) )
					{
						Console.WriteLine("...instruction sequence found.");

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
			if ( ! enableArtifact && difficulty < DifficultyIndex.Eclipse8 )
				return false;

			bool curse = true;
			if ( enableLog.Value ) PrintInfo(damageInfo);

			if ( damageInfo.attacker is null )
			{
				if ( damageInfo.damageType.HasFlag(DamageType.FallDamage) )
					curse = fallDamage.Value;
				else switch ( damageInfo.damageType )
				{
					case DamageType.NonLethal:
					case DamageType.NonLethal | DamageType.BypassArmor:
						curse = selfDamage.Value;
						break;
				}
			}
			else if ( damageInfo.attacker )
			{
				TeamComponent team = damageInfo.attacker.GetComponent<TeamComponent>();
				if ( team ) switch ( team.teamIndex )
				{
					case TeamIndex.Player:
						curse = friendlyFire.Value;
						break;

					case TeamIndex.Neutral:
						curse = stageHazard.Value;
						break;
				}
				else if ( damageInfo.attacker.GetComponent<IInteractable>() is object )
					curse = interactableCost.Value;
			}

			if ( curse ) return true;
			else
			{
				if ( enableLog.Value ) Console.WriteLine("...preventing curse.");
				return false;
			}
		}

		private static void PrintInfo(DamageInfo damageInfo)
		{
			string attacker = getName(damageInfo.attacker),
					inflictor = getName(damageInfo.inflictor);

			static string getName(GameObject obj)
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
			if ( inflictor != attacker && damageInfo.inflictor is not null )
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
