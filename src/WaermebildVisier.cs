// ---------------------------------------------------------------
//  WaermebildVisier - die Waermebildbrille wirkt auch im Zielfernrohr
//
//  WARUM MAN SONST NICHTS SIEHT: Ein vergroesserndes Visier zeichnet
//  sein Bild mit einer EIGENEN kleinen Kamera (OpticComponentUpdater)
//  und legt es auf die Linse. Waermebild bekommt diese Kamera im Spiel
//  nur, wenn das Visier SELBST ein Waermebildvisier ist - siehe
//  OpticComponentUpdater.CopyComponentFromOptic. Die Brille (T-7) sitzt
//  dagegen nur auf der Hauptkamera. Zusaetzlich wird das Linsenbild
//  mit Waermebild gar nicht erst gezeichnet - das hebt LinseUndPunkt.cs
//  auf. Erst beides zusammen ergibt ein Waermebild im Zielfernrohr.
//
//  WAS WIR HIER TUN: Solange die Brille an ist, schalten wir auch auf
//  der Visierkamera Waermebild ein, mit genau den Werten der Brille.
//  Waermebildvisiere (FLIR, Echo1) lassen wir in Ruhe, die regelt das
//  Spiel selbst.
//
//  LateUpdate statt CopyComponentFromOptic: Letzteres laeuft nur beim
//  Wechsel des Visiers. Die Brille kann man aber mitten im Zielen an-
//  und ausschalten, deshalb pruefen wir jedes Bild.
// ---------------------------------------------------------------
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT.CameraControl;
using HarmonyLib;

namespace WaermebildVisier
{
	[BepInPlugin("com.kaan.waermebildvisier", "WaermebildVisier", "1.1.0")]
	public class WaermebildVisierPlugin : BaseUnityPlugin
	{
		internal static ManualLogSource Log;
		internal static ConfigEntry<bool> Aktiv;
		internal static ConfigEntry<bool> GlasAusblenden;

		private void Awake()
		{
			Log = Logger;
			Aktiv = Config.Bind("Allgemein", "Aktiv", true,
				"Waermebildbrille mit allen Visieren nutzen: Leuchtpunkt von Rotpunkt/Holo und Waermebild im Zielfernrohr.");
			GlasAusblenden = Config.Bind("Allgemein", "Glas ausblenden", true,
				"Bei Waermebild das Glas von Rotpunkt-/Holovisieren ausblenden, damit man hindurchsieht.");
			gameObject.AddComponent<GlasAusblender>();
			Harmony harmony = new Harmony("com.kaan.waermebildvisier");
			harmony.PatchAll(typeof(VisierKameraPatch));
			harmony.PatchAll(typeof(LinseUndPunktPatch));
			Log.LogInfo("WaermebildVisier geladen");
		}
	}

	[HarmonyPatch(typeof(OpticComponentUpdater), nameof(OpticComponentUpdater.LateUpdate))]
	internal static class VisierKameraPatch
	{
		// Haben WIR das Waermebild der Visierkamera eingeschaltet? Nur dann
		// schalten wir es auch wieder aus - ein echtes Waermebildvisier
		// geht uns nichts an.
		private static bool _vonUnsAn;
		private static bool _fehlerGemeldet;

		[HarmonyPostfix]
		private static void Nachher(OpticComponentUpdater __instance)
		{
			try
			{
				ThermalVision visier = __instance.thermalVision;
				OpticSight sicht = __instance._opticSight;
				if (visier == null || sicht == null)
				{
					return;
				}

				ScopeThermalVisionData eigenes = sicht.ScopeData != null ? sicht.ScopeData.ThermalVisionData : null;
				if (eigenes != null && eigenes.ThermalVision)
				{
					_vonUnsAn = false;
					return;
				}

				ThermalVision brille = CameraManager.Instance.ThermalVision;
				bool brilleAn = WaermebildVisierPlugin.Aktiv.Value
					&& brille != null && brille.enabled && brille.On;

				if (brilleAn)
				{
					if (!_vonUnsAn || !visier.enabled || !visier.On)
					{
						Uebernehmen(brille, visier);
						visier.enabled = true;
						visier.On = true;
						if (!_vonUnsAn)
						{
							WaermebildVisierPlugin.Log.LogInfo("Brille an - Waermebild auch im Visier " + sicht.name);
						}
						_vonUnsAn = true;
					}
				}
				else if (_vonUnsAn)
				{
					visier.On = false;
					visier.enabled = false;
					_vonUnsAn = false;
				}
			}
			catch (Exception fehler)
			{
				if (!_fehlerGemeldet)
				{
					_fehlerGemeldet = true;
					WaermebildVisierPlugin.Log.LogError("WaermebildVisier: " + fehler);
				}
			}
		}

		/// <summary>Werte der Brille auf die Visierkamera kopieren - dieselben
		/// Felder, die das Spiel fuer ein echtes Waermebildvisier setzt.</summary>
		private static void Uebernehmen(ThermalVision brille, ThermalVision visier)
		{
			visier.IsGlitch = brille.IsGlitch;
			visier.IsPixelated = brille.IsPixelated;
			visier.IsNoisy = brille.IsNoisy;
			visier.IsMotionBlurred = brille.IsMotionBlurred;
			visier.IsFpsStuck = brille.IsFpsStuck;
			visier.ThermalVisionUtilities = brille.ThermalVisionUtilities;
			visier.StuckFpsUtilities = brille.StuckFpsUtilities;
			visier.MotionBlurUtilities = brille.MotionBlurUtilities;
			visier.GlitchUtilities = brille.GlitchUtilities;
			visier.PixelationUtilities = brille.PixelationUtilities;
			visier.ChromaticAberrationThermalShift = brille.ChromaticAberrationThermalShift;
			visier.UnsharpBias = brille.UnsharpBias;
			visier.UnsharpRadiusBlur = brille.UnsharpRadiusBlur;
		}
	}
}
