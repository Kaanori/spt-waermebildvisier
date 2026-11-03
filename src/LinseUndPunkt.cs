// ---------------------------------------------------------------
//  LinseUndPunkt - Leuchtpunkt und Linsenbild auch mit Waermebild
//
//  DIE EIGENTLICHE SPERRE: Nach der Kantenglaettung zeichnet
//  SSAAPropagator in einem eigenen Schritt ("OutputOptic") zwei Dinge
//  ins fertige Bild: das Linsenbild eines Zielfernrohrs und den
//  Leuchtpunkt von Rotpunkt-/Holovisieren. Der ganze Schritt haengt an
//
//      if (!_thermalVisionIsOn && (_opticLensRenderer != null || _collimatorRenderer != null))
//
//  Mit Waermebild entfaellt er also komplett: Beim Rotpunkt sieht man
//  die Welt durchs Glas, aber keinen Punkt - beim Zielfernrohr fehlt
//  das ganze Linsenbild.
//
//  WAS WIR TUN: In OnRenderImage und FillCommandBuffer wird
//  _thermalVisionIsOn je ZWEIMAL gelesen. Die ERSTE Stelle ist diese
//  Sperre - die biegen wir auf SperreAktiv() um. Die ZWEITE schaltet
//  den Waermebildfilter fuers ganze Bild und bleibt, wie sie ist. So
//  laufen Linse und Punkt ganz normal mit durch den Filter, wie der
//  Rest des Bildes auch.
// ---------------------------------------------------------------
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace WaermebildVisier
{
	[HarmonyPatch]
	internal static class LinseUndPunktPatch
	{
		private static readonly FieldInfo WaermeFeld =
			AccessTools.Field(typeof(SSAAPropagator), nameof(SSAAPropagator._thermalVisionIsOn));

		private static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(SSAAPropagator), nameof(SSAAPropagator.OnRenderImage));
			yield return AccessTools.Method(typeof(SSAAPropagator), nameof(SSAAPropagator.FillCommandBuffer));
		}

		/// <summary>Ersatz fuer die erste Abfrage: true heisst "Linse und
		/// Punkt NICHT zeichnen". Mit aktiver Mod sperren wir nie.</summary>
		public static bool SperreAktiv(SSAAPropagator propagator)
		{
			return propagator._thermalVisionIsOn && !WaermebildVisierPlugin.Aktiv.Value;
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> befehle, MethodBase methode)
		{
			bool ersetzt = false;
			foreach (CodeInstruction befehl in befehle)
			{
				if (!ersetzt && befehl.LoadsField(WaermeFeld))
				{
					// Die Instanz liegt schon auf dem Stapel (ldarg.0 davor) -
					// statt das Feld zu lesen, reichen wir sie an SperreAktiv.
					ersetzt = true;
					yield return new CodeInstruction(OpCodes.Call,
						AccessTools.Method(typeof(LinseUndPunktPatch), nameof(SperreAktiv))).MoveLabelsFrom(befehl);
					continue;
				}
				yield return befehl;
			}
			if (ersetzt)
			{
				WaermebildVisierPlugin.Log.LogInfo("Optik-Sperre bei Waermebild aufgehoben in " + methode.Name);
			}
			else
			{
				WaermebildVisierPlugin.Log.LogError("Optik-Sperre NICHT gefunden in " + methode.Name + " - Spielversion geaendert?");
			}
		}
	}
}
