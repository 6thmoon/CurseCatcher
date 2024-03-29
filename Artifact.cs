﻿using HarmonyLib;
using HG;
using RoR2;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.Networking;
using Resources = CurseCatcher.Properties.Resources;

namespace Local.Eclipse.CurseCatcher
{
	static class Artifact
	{
		private static ArtifactDef definition = null;
		private static int ruleIndex = -1;

		[HarmonyPatch(typeof(ArtifactCatalog), nameof(ArtifactCatalog.Init))]
		[HarmonyPostfix]
		private static void CreateArtifact()
		{
			definition = new ArtifactDef {
					nameToken = "Artifact of Infliction",
					descriptionToken = "Only enemies inflict permanent damage.",

					smallIconSelectedSprite = loadSprite(Resources.enabled),
					smallIconDeselectedSprite = loadSprite(Resources.disabled),

					cachedName = "Curse"
				};

			Sprite loadSprite(byte[] imageData)
			{
				Texture2D texture = new Texture2D(0, 0);
				ImageConversion.LoadImage(texture, imageData);

				return Sprite.Create(
						texture, new Rect(0, 0, texture.width, texture.height),
						pivot: new Vector2(texture.width / 2, texture.height / 2)
					);
			}

			Harmony instance = null;

			RunArtifactManager.onArtifactEnabledGlobal +=
				( RunArtifactManager _, ArtifactDef artifact ) =>
				{
					if ( instance is null && NetworkServer.active && artifact == definition )
						instance = Harmony.CreateAndPatchAll(typeof(Plugin));
				};

			RunArtifactManager.onArtifactDisabledGlobal +=
				( RunArtifactManager _, ArtifactDef artifact ) =>
				{
					if ( artifact == definition )
					{
						instance?.UnpatchSelf();
						instance = null;
					}
				};
		}

		[HarmonyPatch(typeof(RuleCatalog), nameof(RuleCatalog.Init))]
		[HarmonyPostfix]
		private static void AddArtifact()
		{
			if ( definition is null )
			{
				System.Console.WriteLine("ERROR: Invalid catalog initialization order.");
				return;
			}

			definition.artifactIndex = (ArtifactIndex) ArtifactCatalog.artifactDefs.Length;
			++RunArtifactManager.enabledArtifactMaskPool.lengthOfArrays;
			ArrayUtils.ArrayAppend(ref ArtifactCatalog.artifactDefs, definition);

			RuleDef ruleDef = RuleDef.FromArtifact(definition.artifactIndex);

			ruleIndex = RuleCatalog.allRuleDefs.Count;
			ruleDef.globalIndex = ruleIndex;

			RuleCatalog.allRuleDefs.Add(ruleDef);
			RuleCatalog.ruleDefsByGlobalName[ruleDef.globalName] = ruleDef;

			ruleDef.category = RuleCatalog.artifactRuleCategory;
			RuleCatalog.artifactRuleCategory.children.Add(ruleDef);

			for ( int i = 0; i < ruleDef.choices.Count; ++i )
			{
				RuleChoiceDef choice = ruleDef.choices[i];

				choice.localIndex = i;
				choice.globalIndex = RuleCatalog.allChoicesDefs.Count;

				RuleCatalog.allChoicesDefs.Add(choice);
				RuleCatalog.ruleChoiceDefsByGlobalName[choice.globalName] = choice;
			}
		}

		[HarmonyPatch(typeof(EclipseRun), nameof(EclipseRun.OverrideRuleChoices))]
		[HarmonyPostfix]
		private static void ShowInEclipse(
				RuleChoiceMask mustInclude, RuleChoiceMask mustExclude)
		{
			IEnumerable<RuleChoiceDef> choices = ruleIndex >= 0 ? 
					RuleCatalog.GetRuleDef(ruleIndex).choices : new List<RuleChoiceDef>();

			foreach ( RuleChoiceDef choice in choices )
			{
				mustInclude[choice.globalIndex] = false;
				mustExclude[choice.globalIndex] = false;
			}
		}

		[HarmonyPatch(typeof(NetworkExtensions), nameof(NetworkExtensions.Write),
				new Type[] { typeof(NetworkWriter), typeof(RuleBook) })]
		[HarmonyPrefix]
		private static bool WriteRuleBook(NetworkWriter writer, RuleBook src)
		{
			for ( int i = 0; i < src.ruleValues.Length; ++i )
				if ( i != ruleIndex )
					writer.Write(src.ruleValues[i]);

			return false;
		}

		[HarmonyPatch(typeof(NetworkExtensions), nameof(NetworkExtensions.ReadRuleBook))]
		[HarmonyPrefix]
		private static bool ReadRuleBook(NetworkReader reader, RuleBook dest)
		{
			for ( int i = 0; i < dest.ruleValues.Length; ++i )
			{
				if ( i == ruleIndex )
				{
					dest.ruleValues[i] =
							(byte) RuleCatalog.GetRuleDef(ruleIndex).defaultChoiceIndex;
				}
				else dest.ruleValues[i] = reader.ReadByte();
			}

			return false;
		}

		[HarmonyPatch(typeof(RunArtifactManager), nameof(RunArtifactManager.OnDeserialize))]
		[HarmonyTranspiler]
		private static IEnumerable<CodeInstruction> LimitReadLength(
				IEnumerable<CodeInstruction> instructionList)
		{
			MethodInfo readBitArray = typeof(NetworkExtensions).GetMethod(
					nameof(NetworkExtensions.ReadBitArray),
					new Type[] { typeof(NetworkReader), typeof(bool[]) }
				);

			foreach ( CodeInstruction instruction in instructionList )
			{
				if ( instruction.Calls(readBitArray) )
				{
					yield return Transpilers.EmitDelegate<Action<NetworkReader, bool[]>>(
						( NetworkReader input, bool[] array ) =>
						{
							if ( definition is null ) input.ReadBitArray(array);
							else input.ReadBitArray(array, array.Length - 1);
						});
				}
				else yield return instruction;
			}
		}

		[HarmonyPatch(typeof(PreGameRuleVoteController),
				nameof(PreGameRuleVoteController.ReadVotes))]
		[HarmonyTranspiler]
		private static IEnumerable<CodeInstruction> LimitRuleCount(
				IEnumerable<CodeInstruction> instructionList)
		{
			MethodInfo ruleCount = typeof(RuleCatalog).GetProperty(
					nameof(RuleCatalog.ruleCount)
				).GetMethod;

			foreach ( CodeInstruction instruction in instructionList )
			{
				yield return instruction;

				if ( instruction.Calls(ruleCount) )
				{
					yield return new CodeInstruction(OpCodes.Ldc_I4_1);
					yield return new CodeInstruction(OpCodes.Sub);
				}
			}
		}
	}
}
