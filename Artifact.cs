using HarmonyLib;
using HG;
using RoR2;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using Resources = CurseCatcher.Properties.Resources;

namespace Local.Eclipse.CurseCatcher;

static class Artifact
{
	static ArtifactDef definition = null;
	static int ruleIndex = -1;

	[HarmonyPatch(typeof(ArtifactCatalog), nameof(ArtifactCatalog.SetArtifactDefs))]
	[HarmonyPostfix]
	static void CreateArtifact()
	{
		definition = new ArtifactDef {
				nameToken = "Artifact of Infliction",
				descriptionToken = "Only enemies inflict permanent damage.",

				smallIconSelectedSprite = Load(Resources.enabled),
				smallIconDeselectedSprite = Load(Resources.disabled),

				artifactIndex = (ArtifactIndex) ArtifactCatalog.artifactDefs.Length,
				cachedName = "Curse"
			};

		ArrayUtils.ArrayAppend(ref ArtifactCatalog.artifactDefs, definition);
		Harmony instance = null;

		RunArtifactManager.onArtifactEnabledGlobal +=
			( RunArtifactManager _, ArtifactDef artifact ) =>
			{
				if ( instance is null && NetworkServer.active && artifact == definition )
					instance = Harmony.CreateAndPatchAll(Plugin.instance.GetType());
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

	static Sprite Load(byte[] imageData)
	{
		Texture2D texture = new(512, 512, TextureFormat.ARGB32, 4, linear: false);
		ImageConversion.LoadImage(texture, imageData);

		return Sprite.Create(
				texture, new Rect(0, 0, texture.width, texture.height),
				pivot: new Vector2(texture.width / 2, texture.height / 2)
			);
	}

	[HarmonyPatch(typeof(RuleCatalog), nameof(RuleCatalog.Init))]
	[HarmonyFinalizer]
	static void AddArtifact(Exception __exception)
	{
		if ( __exception is not null ) throw __exception;
		RuleDef rule = RuleDef.FromArtifact(definition.artifactIndex);

		ruleIndex = RuleCatalog.allRuleDefs.Count;
		rule.globalIndex = ruleIndex;

		RuleCatalog.allRuleDefs.Add(rule);
		RuleCatalog.ruleDefsByGlobalName[rule.globalName] = rule;

		rule.category = RuleCatalog.artifactRuleCategory;
		RuleCatalog.artifactRuleCategory.children.Add(rule);

		for ( int i = 0; i < rule.choices.Count; ++i )
		{
			RuleChoiceDef choice = rule.choices[i];

			choice.localIndex = i;
			choice.globalIndex = RuleCatalog.allChoicesDefs.Count;

			RuleCatalog.allChoicesDefs.Add(choice);
			RuleCatalog.ruleChoiceDefsByGlobalName[choice.globalName] = choice;
		}
	}

	[HarmonyPatch(typeof(RuleCatalog), nameof(RuleCatalog.AddRule))]
	[HarmonyPrefix]
	static bool SkipRule(RuleDef ruleDef)
	{
		foreach ( RuleChoiceDef choice in ruleDef.choices )
			if ( choice.artifactIndex == definition.artifactIndex )
				return false;

		return true;
	}

	[HarmonyPatch(typeof(EclipseRun), nameof(EclipseRun.OverrideRuleChoices))]
	[HarmonyPostfix]
	static void ShowInEclipse(
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
	static bool WriteRuleBook(NetworkWriter writer, RuleBook src)
	{
		for ( int i = 0; i < src.ruleValues.Length; ++i )
			if ( i != ruleIndex )
				writer.Write(src.ruleValues[i]);

		return false;
	}

	[HarmonyPatch(typeof(NetworkExtensions), nameof(NetworkExtensions.ReadRuleBook))]
	[HarmonyPrefix]
	static bool ReadRuleBook(NetworkReader reader, RuleBook dest)
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
	static IEnumerable<CodeInstruction> LimitReadLength(IEnumerable<CodeInstruction> IL)
	{
		MethodInfo readBitArray = typeof(NetworkExtensions).GetMethod(
				nameof(NetworkExtensions.ReadBitArray),
				new Type[] { typeof(NetworkReader), typeof(bool[]) }
			);

		foreach ( CodeInstruction instruction in IL )
		{
			if ( instruction.Calls(readBitArray) )
			{
				yield return Transpilers.EmitDelegate(
					( NetworkReader input, bool[] array ) =>
					{
						if ( definition is null ) input.ReadBitArray(array);
						else input.ReadBitArray(array, array.Length - 1);
					});
			}
			else yield return instruction;
		}
	}
}
