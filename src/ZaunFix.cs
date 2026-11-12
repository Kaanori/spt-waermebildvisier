// ---------------------------------------------------------------
//  ZaunFix - Maschendraht bleibt im Waermebild durchsichtig
//
//  URSACHE (Befund 12.11.2026, Shader-Disassembly):
//  Der Spiel-Shader "p0/Reflective/Bumped Specular SMap Transparent
//  Cutoff" (Bauzaun fence_ema_01_blue/_white, Cardinal-Zaun, viele
//  LOD3-Modelle) rechnet im Waermebild-Zweig  farbe = float4(temp, 1)
//  (Alpha fest 1) und macht den Alpha-Test (clip gegen _Cutoff) ERST
//  DANACH. Es faellt nie ein Pixel weg - der Zaun wird eine Flaeche.
//  "p0/Cutout/Bumped Diffuse" testet VOR dem Waermebild-Zweig.
//
//  WAS WIR TUN: Nur waehrend eine Waermebild-Kamera rendert (Brille ODER
//  Visierkamera: ThermalVision.OnPreCull ... OnPostRender) bekommen die
//  betroffenen Materialien den Cutout-Shader, danach sofort den
//  Originalshader zurueck. _Temperature2 wird solange nach _Temperature
//  kopiert, damit der Zaun gleich "warm" bleibt.
//
//  Robust: Materialliste neu bei jedem Szenenwechsel, null-sicher, keine
//  Allokationen pro Bild, mehrere Kameras zaehlen mit (Liste aktiver
//  Kameras), Rueckgabe auch bei Ausnahmen und beim Szenenwechsel.
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WaermebildVisier
{
	internal static class ZaunFix
	{
		private const string FehlerShader = "p0/Reflective/Bumped Specular SMap Transparent Cutoff";
		private const string ErsatzShader = "p0/Cutout/Bumped Diffuse";

		private static readonly int TempId = Shader.PropertyToID("_Temperature");
		private static readonly int Temp2Id = Shader.PropertyToID("_Temperature2");

		private struct Eintrag
		{
			public Material Mat;
			public Shader Original;
			public Vector4 Temp;
			public bool HatTemp;
			public bool HatTemp2;
		}

		private static bool _veraltet = true;
		private static bool _getauscht;
		private static bool _fehlerGemeldet;
		private static Shader _ersatz;
		private static readonly List<Eintrag> _liste = new List<Eintrag>();
		// Kameras, die gerade ein Waermebild rendern (klein, keine Allokation nach dem Start)
		private static readonly List<ThermalVision> _aktiv = new List<ThermalVision>(4);

		internal static void Starten()
		{
			SceneManager.sceneLoaded += (s, m) => NeuerSzene();
		}

		private static void NeuerSzene()
		{
			try
			{
				// Zuerst zurueck, solange die Liste noch gilt (Materialien aus Bundles ueberleben Szenen)
				Alles();
			}
			finally
			{
				_aktiv.Clear();
				_veraltet = true;
			}
		}

		// Einmal je Szene alle betroffenen Materialien suchen - nie pro Bild.
		private static void Sammeln()
		{
			_veraltet = false;
			_liste.Clear();
			Material[] alle = Resources.FindObjectsOfTypeAll<Material>();
			for (int i = 0; i < alle.Length; i++)
			{
				Material m = alle[i];
				if (m == null) continue;
				Shader s = m.shader;
				if (s == null) continue;
				string name = s.name;
				if (_ersatz == null && name == ErsatzShader) _ersatz = s;
				if (name != FehlerShader) continue;
				_liste.Add(new Eintrag
				{
					Mat = m,
					Original = s,
					HatTemp = m.HasProperty(TempId),
					HatTemp2 = m.HasProperty(Temp2Id)
				});
			}
			if (_ersatz == null) _ersatz = Shader.Find(ErsatzShader);
			WaermebildVisierPlugin.Log.LogInfo(_liste.Count + " Materialien mit fehlerhaftem Alpha-Test im Waermebild gefunden, Ersatzshader "
				+ (_ersatz != null ? "da" : "FEHLT"));
		}

		internal static void KameraAn(ThermalVision kamera)
		{
			try
			{
				if (kamera == null || !kamera.On || !WaermebildVisierPlugin.ZaunReparieren.Value) return;
				if (!_aktiv.Contains(kamera)) _aktiv.Add(kamera);
				if (!_getauscht) Tauschen();
			}
			catch (Exception fehler)
			{
				Melden(fehler);
			}
		}

		internal static void KameraAus(ThermalVision kamera)
		{
			try
			{
				_aktiv.Remove(kamera);
				if (_aktiv.Count == 0) Alles();
			}
			catch (Exception fehler)
			{
				Melden(fehler);
			}
		}

		private static void Tauschen()
		{
			if (_veraltet) Sammeln();
			if (_ersatz == null || _liste.Count == 0) return;
			// Vorher setzen: bei einer Ausnahme mitten im Lauf holt Zurueck alles zurueck.
			_getauscht = true;
			for (int i = 0; i < _liste.Count; i++)
			{
				Eintrag e = _liste[i];
				if (e.Mat == null) continue;
				// Werte VOR dem Tausch lesen (aktueller Stand, nicht der vom Einsammeln)
				if (e.HatTemp) e.Temp = e.Mat.GetVector(TempId);
				Vector4 t2 = e.HatTemp2 ? e.Mat.GetVector(Temp2Id) : e.Temp;
				_liste[i] = e;
				e.Mat.shader = _ersatz;
				e.Mat.SetVector(TempId, t2);
			}
		}

		// Alles auf den Originalzustand - jeder Eintrag einzeln gesichert.
		private static void Alles()
		{
			if (!_getauscht) return;
			_getauscht = false;
			for (int i = 0; i < _liste.Count; i++)
			{
				try
				{
					Eintrag e = _liste[i];
					if (e.Mat == null) continue;
					e.Mat.shader = e.Original;
					if (e.HatTemp) e.Mat.SetVector(TempId, e.Temp);
				}
				catch (Exception fehler)
				{
					Melden(fehler);
				}
			}
		}

		private static void Melden(Exception fehler)
		{
			if (_fehlerGemeldet) return;
			_fehlerGemeldet = true;
			WaermebildVisierPlugin.Log.LogError("ZaunFix: " + fehler);
		}
	}

	// ThermalVision haengt an jeder Kamera mit Waermebild (Brille = Hauptkamera,
	// Visierkamera). OnPreCull/OnPostRender laufen je Kamera und Bild.
	[HarmonyPatch(typeof(ThermalVision))]
	internal static class ZaunThermalPatch
	{
		[HarmonyPostfix, HarmonyPatch(nameof(ThermalVision.OnPreCull))]
		private static void VorDemRendern(ThermalVision __instance)
		{
			ZaunFix.KameraAn(__instance);
		}

		[HarmonyPostfix, HarmonyPatch(nameof(ThermalVision.OnPostRender))]
		private static void NachDemRendern(ThermalVision __instance)
		{
			ZaunFix.KameraAus(__instance);
		}

		[HarmonyPostfix, HarmonyPatch(nameof(ThermalVision.OnDisable))]
		private static void Aus(ThermalVision __instance)
		{
			ZaunFix.KameraAus(__instance);
		}
	}
}
