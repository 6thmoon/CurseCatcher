using HarmonyLib;
using RoR2;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using Resources = CurseCatcher.Properties.Resources;

namespace Local.Eclipse.CurseCatcher
{
	static class Artifact
	{
		public static bool? Enabled => definition is null ? null :
				RunArtifactManager.instance?.IsArtifactEnabled(definition);

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

					artifactIndex = (ArtifactIndex) ArtifactCatalog.artifactDefs.Length,
					cachedName = "Curse"
				};

			static Sprite loadSprite(byte[] imageData)
			{
				Texture2D texture = new(512, 512, TextureFormat.ARGB32, 4, linear: false);
				ImageConversion.LoadImage(texture, imageData);

				return Sprite.Create(
						texture, new Rect(0, 0, texture.width, texture.height),
						pivot: new Vector2(texture.width / 2, texture.height / 2)
					);
			}
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

		[HarmonyPatch(typeof(ArtifactCatalog), nameof(ArtifactCatalog.GetArtifactDef))]
		[HarmonyPrefix]
		private static bool GetArtifact(ArtifactIndex artifactIndex,
				ref ArtifactDef __result)
		{
			__result = definition;
			return artifactIndex != definition?.artifactIndex;
		}

		[HarmonyPatch(typeof(ArtifactCatalog), nameof(ArtifactCatalog.FindArtifactIndex))]
		[HarmonyPrefix]
		private static bool FindArtifact(string artifactName,
				ref ArtifactIndex __result)
		{
			__result = definition?.artifactIndex ?? ArtifactIndex.None;
			return artifactName != definition?.cachedName;
		}

		[HarmonyPatch(typeof(ArtifactCatalog),
				nameof(ArtifactCatalog.artifactCount), MethodType.Getter)]
		[HarmonyPostfix]
		private static int CountArtifact(int artifactCount) 
				=> artifactCount + ( definition is null ? 0 : 1 );

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
				new System.Type[] { typeof(NetworkWriter), typeof(RuleBook) })]
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
					new System.Type[] { typeof(NetworkReader), typeof(bool[]) }
				);

			foreach ( CodeInstruction instruction in instructionList )
			{
				if ( instruction.Calls(readBitArray) )
				{
					yield return CodeInstruction.Call(
							typeof(Artifact), nameof(Artifact.ReadEnabledArtifacts));
				}
				else yield return instruction;
			}
		}

		private static void ReadEnabledArtifacts(NetworkReader reader, bool[] values)
				=> reader.ReadBitArray(values, ArtifactCatalog.artifactDefs.Length);
	}
}
