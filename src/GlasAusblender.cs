// ---------------------------------------------------------------
//  GlasAusblender - das Rotpunkt-/Hologlas verdeckt das Waermebild
//
//  BEFUND (Test 2026-09-22, EOTech): Mit LinseUndPunkt.cs ist der
//  Leuchtpunkt bei Waermebild wieder da - aber das Fenster des Visiers
//  ist komplett schwarz, waehrend man daneben die Baeume sieht. Das
//  Glas wird im Waermebild also wie eine feste, kalte Flaeche
//  gezeichnet. Wie genau, steht im Shader des Glases, und den koennen
//  wir nicht lesen.
//
//  VERSUCH: Solange Waermebild an ist, schieben wir den Renderer des
//  aktiven Rotpunktglases auf eine Ebene (Layer), die die Hauptkamera
//  nicht zeichnet. Den Leuchtpunkt malt SSAAPropagator trotzdem - er
//  nutzt CommandBuffer.DrawRenderer, und das fragt die Ebene nicht ab.
//  Beim Ausschalten kommt die alte Ebene zurueck.
//
//  DIAGNOSE: Ist die schwarze Flaeche ein ANDERES Bauteil als das
//  Rotpunktglas, bringt der Versuch nichts. Deshalb schreiben wir beim
//  ersten Zielen mit Waermebild alle Bauteile des Visiers ins Log -
//  Name, Shader, Ebene -, damit wir das richtige finden.
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Text;
using EFT.CameraControl;
using UnityEngine;

namespace WaermebildVisier
{
	internal class GlasAusblender : MonoBehaviour
	{
		private Renderer _ausgeblendet;
		private int _alteEbene;
		private readonly HashSet<int> _protokolliert = new HashSet<int>();
		private bool _fehlerGemeldet;

		private void LateUpdate()
		{
			try
			{
				CameraManager kameras = CameraManager.Instance;
				ThermalVision brille = kameras.ThermalVision;
				Camera haupt = kameras.Camera;
				bool waermeAn = WaermebildVisierPlugin.Aktiv.Value
					&& WaermebildVisierPlugin.GlasAusblenden.Value
					&& brille != null && brille.enabled && brille.On && haupt != null;
				Renderer glas = waermeAn && kameras.SSAA != null ? kameras.SSAA.GetCollimatorRenderer() : null;

				if (glas != _ausgeblendet)
				{
					Zurueck();
					if (glas != null)
					{
						int frei = FreieEbene(haupt);
						Protokollieren(glas, frei);
						if (frei >= 0)
						{
							_alteEbene = glas.gameObject.layer;
							glas.gameObject.layer = frei;
							_ausgeblendet = glas;
						}
					}
				}
			}
			catch (Exception fehler)
			{
				if (!_fehlerGemeldet)
				{
					_fehlerGemeldet = true;
					WaermebildVisierPlugin.Log.LogError("GlasAusblender: " + fehler);
				}
			}
		}

		private void OnDisable()
		{
			Zurueck();
		}

		private void Zurueck()
		{
			if (_ausgeblendet != null)
			{
				_ausgeblendet.gameObject.layer = _alteEbene;
			}
			_ausgeblendet = null;
		}

		/// <summary>Eine Ebene, die die Hauptkamera NICHT zeichnet. Ebene 0
		/// (Default) lassen wir aus, die ist fast immer sichtbar.</summary>
		private static int FreieEbene(Camera haupt)
		{
			for (int ebene = 31; ebene > 0; ebene--)
			{
				if ((haupt.cullingMask & (1 << ebene)) == 0)
				{
					return ebene;
				}
			}
			return -1;
		}

		private void Protokollieren(Renderer glas, int frei)
		{
			Transform wurzel = glas.transform;
			for (int i = 0; i < 3 && wurzel.parent != null; i++)
			{
				wurzel = wurzel.parent;
			}
			if (!_protokolliert.Add(wurzel.GetInstanceID()))
			{
				return;
			}

			StringBuilder text = new StringBuilder();
			text.AppendLine("Visier mit Waermebild: Rotpunktglas '" + glas.name + "' Ebene "
				+ glas.gameObject.layer + " -> " + frei + ", Bauteile unter '" + wurzel.name + "':");
			int anzahl = 0;
			foreach (Renderer teil in wurzel.GetComponentsInChildren<Renderer>(true))
			{
				if (++anzahl > 60)
				{
					text.AppendLine("   ... (gekuerzt)");
					break;
				}
				text.Append("   ").Append(teil == glas ? "* " : "  ")
					.Append(Pfad(teil.transform, wurzel))
					.Append(" | Ebene ").Append(teil.gameObject.layer)
					.Append(" | an=").Append(teil.enabled && teil.gameObject.activeInHierarchy);
				foreach (Material material in teil.sharedMaterials)
				{
					if (material != null)
					{
						text.Append(" | ").Append(material.name).Append(" [")
							.Append(material.shader != null ? material.shader.name : "?")
							.Append(", Queue ").Append(material.renderQueue).Append("]");
					}
				}
				text.AppendLine();
			}
			WaermebildVisierPlugin.Log.LogInfo(text.ToString());
		}

		private static string Pfad(Transform teil, Transform wurzel)
		{
			string pfad = teil.name;
			while (teil != wurzel && teil.parent != null)
			{
				teil = teil.parent;
				pfad = teil.name + "/" + pfad;
			}
			return pfad;
		}
	}
}
