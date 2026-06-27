using System;
using System.Collections.Generic;
using System.Globalization;

namespace LcdFusion
{
    // Lightweight in-memory localization. Add a language by adding a dictionary to _all
    // and an entry to Codes/Names — every UI string goes through Loc.T(key).
    internal static class Loc
    {
        public static readonly string[] Codes = { "it", "en", "de" };
        public static readonly string[] Names = { "Italiano", "English", "Deutsch" };

        public static string Lang = "it";
        public static event Action Changed;

        static Loc()
        {
            try
            {
                string sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                if (Array.IndexOf(Codes, sys) >= 0) Lang = sys;
            }
            catch { }
        }

        public static void Set(string code)
        {
            if (code == Lang || Array.IndexOf(Codes, code) < 0) return;
            Lang = code;
            if (Changed != null) Changed();
        }

        public static int CurrentIndex()
        {
            int i = Array.IndexOf(Codes, Lang);
            return i < 0 ? 0 : i;
        }

        public static string T(string key)
        {
            Dictionary<string, string> d;
            string v;
            if (_all.TryGetValue(Lang, out d) && d.TryGetValue(key, out v)) return v;
            if (_all["it"].TryGetValue(key, out v)) return v;
            return key;
        }

        public static string T(string key, object arg0)
        {
            return string.Format(T(key), arg0);
        }

        private static readonly Dictionary<string, string> It = new Dictionary<string, string>
        {
            { "app.subtitle", "Controller LCD · Valkyrie + Thermalright" },
            { "btn.free", "Libera dispositivi" },
            { "dev.ready", "pronto" }, { "dev.error", "errore" }, { "dev.absent", "assente" },
            { "sum.both", "Entrambi gli LCD sono pronti" },
            { "sum.count", "{0} di 2 LCD disponibili" },
            { "sum.busy", "Chiudi {0} per liberare l'USB" },
            { "sum.updated", "aggiornato" },
            { "tgt.stream", "Trasmetti su questo schermo" },
            { "sec.background", "SFONDO" }, { "sec.layers", "LIVELLI" }, { "sec.preview", "ANTEPRIMA" },
            { "bg.image", "Immagine" }, { "bg.color", "Colore" }, { "bg.none", "Nessuno" },
            { "tf.mirror", "Specchia" }, { "tf.fit", "Adatta" }, { "tf.fill", "Riempi" }, { "tf.reset", "Reset" },
            { "sl.zoom", "Zoom" }, { "sl.rotation", "Rotazione" }, { "sl.size", "Dimensione" },
            { "sl.x", "X" }, { "sl.y", "Y" },
            { "screen.copyTo", "Copia su {0}" }, { "screen.copiedTo", "Layout copiato su {0}" },
            { "ly.addSensor", "+ Sensore" }, { "ly.addText", "+ Testo" }, { "ly.empty", "Nessun livello: aggiungi un sensore o un testo." },
            { "ov.type", "Tipo" }, { "ov.color", "Colore" }, { "ov.label", "Etichetta" },
            { "ov.center", "Centra" }, { "ov.remove", "Rimuovi livello" }, { "ov.moveUp", "Sposta sopra" },
            { "ov.moveDown", "Sposta sotto" }, { "ov.duplicate", "Duplica livello" }, { "ov.properties", "Proprieta" },
            { "metric.cpuTemp", "Temp CPU" }, { "metric.gpuTemp", "Temp GPU" },
            { "metric.cpuLoad", "Carico CPU" }, { "metric.gpuLoad", "Carico GPU" },
            { "metric.cpuClock", "Clock CPU" }, { "metric.gpuClock", "Clock GPU" }, { "metric.cpuPower", "Potenza CPU" }, { "metric.gpuPower", "Potenza GPU" },
            { "metric.gpuVram", "VRAM" }, { "metric.gpuFan", "Ventola GPU" }, { "metric.ramLoad", "RAM" }, { "metric.cpuCores", "Core CPU (grafico)" },
            { "metric.clock", "Orologio" }, { "metric.date", "Data" }, { "metric.text", "Testo" },
            { "color.white", "Bianco" }, { "color.cyan", "Ciano" }, { "color.green", "Verde" },
            { "color.amber", "Ambra" }, { "color.red", "Rosso" }, { "color.blue", "Blu" },
            { "color.palette", "Palette" }, { "color.custom", "Colore personalizzato" }, { "color.recent", "Colore recente" },
            { "text.default", "Testo" }, { "layer.textPrefix", "Testo: " },
            { "preview.hint", "Trascina gli elementi per spostarli; trascina lo sfondo per posizionarlo." },
            { "btn.start", "Avvia streaming" }, { "btn.stop", "Ferma" },
            { "st.ready", "Pronto" }, { "st.starting", "Avvio streaming..." },
            { "st.stopped", "Streaming fermato" }, { "st.active", "Streaming attivo" },
            { "st.freed", "Dispositivi liberati" }, { "st.freeing", "Chiusura software originali..." },
            { "st.freeFail", "Impossibile chiudere Myth.Cool / TRCC" },
            { "msg.noTarget", "Attiva almeno uno schermo con la casella sotto la scheda." },
            { "dlg.pickGif", "Scegli una GIF" }, { "dlg.pickImage", "Scegli un'immagine" },
            { "dlg.images", "Immagini" },
            { "err.image", "Immagine non valida: " }, { "err.gif", "GIF non valida: " },
            { "prof.save", "Salva" }, { "prof.unsaved", "Modifiche non salvate" },
            { "prof.deleteTitle", "Elimina profilo" }, { "prof.deleteConfirm", "Eliminare il profilo \"{0}\"?" },
            { "prof.label", "Profilo" }, { "prof.load", "Carica" }, { "prof.saveAs", "Salva come…" },
            { "prof.delete", "Elimina" }, { "prof.autostart", "Avvia con Windows" }, { "prof.none", "(nessun profilo)" },
            { "prof.nameTitle", "Nome del profilo" }, { "prof.saved", "Profilo salvato" }, { "prof.loaded", "Profilo caricato" },
            { "prof.deleted", "Profilo eliminato" }, { "dlg.ok", "OK" }, { "dlg.cancel", "Annulla" },
            { "tray.open", "Apri" }, { "tray.exit", "Esci" },
            { "tray.hint", "LCD Fusion resta attivo nell'area di notifica: lo streaming continua." },
            { "lang.label", "Lingua" },
            { "btn.about", "Info" }, { "dlg.close", "Chiudi" },
            { "about.title", "Info e legalita" }, { "about.tab.project", "Progetto" }, { "about.tab.legal", "Legalita" }, { "about.tab.licenses", "Licenze" },
            { "about.subtitle", "Controller LCD open source per schermi Valkyrie e Thermalright." },
            { "about.version", "Versione" }, { "about.developer", "Sviluppatore" }, { "about.license", "Licenza" }, { "about.repository", "Repository" },
            { "about.opensource", "Il codice sorgente e distribuito con licenza MIT. I componenti terzi mantengono le rispettive licenze." },
            { "about.vendorDisclaimer", "Progetto non ufficiale: non affiliato, approvato o sponsorizzato da Valkyrie, Thermalright, Myth.Cool o TRCC." },
            { "legal.unofficial", "LCD Fusion e un progetto community non ufficiale. I nomi dei vendor sono usati solo per indicare compatibilita hardware." },
            { "legal.interop", "Interoperabilita: il protocollo e stato ricostruito osservando traffico USB di dispositivi posseduti, senza copiare codice, firmware o asset dei vendor." },
            { "legal.trademarks", "Marchi: Valkyrie, Thermalright, Myth.Cool e TRCC appartengono ai rispettivi proprietari." },
            { "legal.noTelemetry", "Privacy: l'app non usa telemetria e non effettua chiamate di rete." },
            { "legal.risk", "Uso a proprio rischio: pilotare direttamente l'LCD puo avere effetti imprevisti sul dispositivo o sulla garanzia." },
            { "legal.fileMissing", "File legal non trovato nella cartella dell'app." },
        };

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "app.subtitle", "LCD controller · Valkyrie + Thermalright" },
            { "btn.free", "Release devices" },
            { "dev.ready", "ready" }, { "dev.error", "error" }, { "dev.absent", "absent" },
            { "sum.both", "Both LCDs are ready" },
            { "sum.count", "{0} of 2 LCDs available" },
            { "sum.busy", "Close {0} to free the USB" },
            { "sum.updated", "updated" },
            { "tgt.stream", "Stream to this screen" },
            { "sec.background", "BACKGROUND" }, { "sec.layers", "LAYERS" }, { "sec.preview", "PREVIEW" },
            { "bg.image", "Image" }, { "bg.color", "Color" }, { "bg.none", "None" },
            { "tf.mirror", "Mirror" }, { "tf.fit", "Fit" }, { "tf.fill", "Fill" }, { "tf.reset", "Reset" },
            { "sl.zoom", "Zoom" }, { "sl.rotation", "Rotation" }, { "sl.size", "Size" },
            { "sl.x", "X" }, { "sl.y", "Y" },
            { "screen.copyTo", "Copy to {0}" }, { "screen.copiedTo", "Layout copied to {0}" },
            { "ly.addSensor", "+ Sensor" }, { "ly.addText", "+ Text" }, { "ly.empty", "No layers yet: add a sensor or text." },
            { "ov.type", "Type" }, { "ov.color", "Color" }, { "ov.label", "Label" },
            { "ov.center", "Center" }, { "ov.remove", "Remove layer" }, { "ov.moveUp", "Move up" },
            { "ov.moveDown", "Move down" }, { "ov.duplicate", "Duplicate layer" }, { "ov.properties", "Properties" },
            { "metric.cpuTemp", "CPU temp" }, { "metric.gpuTemp", "GPU temp" },
            { "metric.cpuLoad", "CPU load" }, { "metric.gpuLoad", "GPU load" },
            { "metric.cpuClock", "CPU clock" }, { "metric.gpuClock", "GPU clock" }, { "metric.cpuPower", "CPU power" }, { "metric.gpuPower", "GPU power" },
            { "metric.gpuVram", "VRAM" }, { "metric.gpuFan", "GPU fan" }, { "metric.ramLoad", "RAM" }, { "metric.cpuCores", "CPU cores (graph)" },
            { "metric.clock", "Clock" }, { "metric.date", "Date" }, { "metric.text", "Text" },
            { "color.white", "White" }, { "color.cyan", "Cyan" }, { "color.green", "Green" },
            { "color.amber", "Amber" }, { "color.red", "Red" }, { "color.blue", "Blue" },
            { "color.palette", "Palette" }, { "color.custom", "Custom color" }, { "color.recent", "Recent color" },
            { "text.default", "Text" }, { "layer.textPrefix", "Text: " },
            { "preview.hint", "Drag elements to move them; drag the background to position it." },
            { "btn.start", "Start streaming" }, { "btn.stop", "Stop" },
            { "st.ready", "Ready" }, { "st.starting", "Starting streaming..." },
            { "st.stopped", "Streaming stopped" }, { "st.active", "Streaming active" },
            { "st.freed", "Devices released" }, { "st.freeing", "Closing original software..." },
            { "st.freeFail", "Cannot close Myth.Cool / TRCC" },
            { "msg.noTarget", "Enable at least one screen using the checkbox under the tab." },
            { "dlg.pickGif", "Choose a GIF" }, { "dlg.pickImage", "Choose an image" },
            { "dlg.images", "Images" },
            { "err.image", "Invalid image: " }, { "err.gif", "Invalid GIF: " },
            { "prof.save", "Save" }, { "prof.unsaved", "Unsaved changes" },
            { "prof.deleteTitle", "Delete profile" }, { "prof.deleteConfirm", "Delete profile \"{0}\"?" },
            { "prof.label", "Profile" }, { "prof.load", "Load" }, { "prof.saveAs", "Save as…" },
            { "prof.delete", "Delete" }, { "prof.autostart", "Launch at Windows startup" }, { "prof.none", "(no profile)" },
            { "prof.nameTitle", "Profile name" }, { "prof.saved", "Profile saved" }, { "prof.loaded", "Profile loaded" },
            { "prof.deleted", "Profile deleted" }, { "dlg.ok", "OK" }, { "dlg.cancel", "Cancel" },
            { "tray.open", "Open" }, { "tray.exit", "Exit" },
            { "tray.hint", "LCD Fusion keeps running in the tray: streaming continues." },
            { "lang.label", "Language" },
            { "btn.about", "About" }, { "dlg.close", "Close" },
            { "about.title", "About and legal" }, { "about.tab.project", "Project" }, { "about.tab.legal", "Legal" }, { "about.tab.licenses", "Licenses" },
            { "about.subtitle", "Open-source LCD controller for Valkyrie and Thermalright screens." },
            { "about.version", "Version" }, { "about.developer", "Developer" }, { "about.license", "License" }, { "about.repository", "Repository" },
            { "about.opensource", "The source code is distributed under the MIT license. Third-party components keep their own licenses." },
            { "about.vendorDisclaimer", "Unofficial project: not affiliated with, endorsed by, or sponsored by Valkyrie, Thermalright, Myth.Cool, or TRCC." },
            { "legal.unofficial", "LCD Fusion is an unofficial community project. Vendor names are used only to describe hardware compatibility." },
            { "legal.interop", "Interoperability: the protocol was rebuilt by observing USB traffic from owned devices, without copying vendor code, firmware, or assets." },
            { "legal.trademarks", "Trademarks: Valkyrie, Thermalright, Myth.Cool, and TRCC belong to their respective owners." },
            { "legal.noTelemetry", "Privacy: the app has no telemetry and makes no network calls." },
            { "legal.risk", "Use at your own risk: driving the LCD directly may have unexpected effects on the device or warranty." },
            { "legal.fileMissing", "Legal file not found in the app folder." },
        };

        private static readonly Dictionary<string, string> De = new Dictionary<string, string>
        {
            { "app.subtitle", "LCD-Controller · Valkyrie + Thermalright" },
            { "btn.free", "Geräte freigeben" },
            { "dev.ready", "bereit" }, { "dev.error", "Fehler" }, { "dev.absent", "fehlt" },
            { "sum.both", "Beide LCDs sind bereit" },
            { "sum.count", "{0} von 2 LCDs verfügbar" },
            { "sum.busy", "{0} schließen, um USB freizugeben" },
            { "sum.updated", "aktualisiert" },
            { "tgt.stream", "Auf diesen Bildschirm streamen" },
            { "sec.background", "HINTERGRUND" }, { "sec.layers", "EBENEN" }, { "sec.preview", "VORSCHAU" },
            { "bg.image", "Bild" }, { "bg.color", "Farbe" }, { "bg.none", "Keiner" },
            { "tf.mirror", "Spiegeln" }, { "tf.fit", "Anpassen" }, { "tf.fill", "Füllen" }, { "tf.reset", "Zurücksetzen" },
            { "sl.zoom", "Zoom" }, { "sl.rotation", "Drehung" }, { "sl.size", "Größe" },
            { "sl.x", "X" }, { "sl.y", "Y" },
            { "screen.copyTo", "Nach {0} kopieren" }, { "screen.copiedTo", "Layout nach {0} kopiert" },
            { "ly.addSensor", "+ Sensor" }, { "ly.addText", "+ Text" }, { "ly.empty", "Noch keine Ebenen: Sensor oder Text hinzufuegen." },
            { "ov.type", "Typ" }, { "ov.color", "Farbe" }, { "ov.label", "Beschriftung" },
            { "ov.center", "Zentrieren" }, { "ov.remove", "Ebene entfernen" }, { "ov.moveUp", "Nach oben" },
            { "ov.moveDown", "Nach unten" }, { "ov.duplicate", "Ebene duplizieren" }, { "ov.properties", "Eigenschaften" },
            { "metric.cpuTemp", "CPU-Temp" }, { "metric.gpuTemp", "GPU-Temp" },
            { "metric.cpuLoad", "CPU-Last" }, { "metric.gpuLoad", "GPU-Last" },
            { "metric.cpuClock", "CPU-Takt" }, { "metric.gpuClock", "GPU-Takt" }, { "metric.cpuPower", "CPU-Leistung" }, { "metric.gpuPower", "GPU-Leistung" },
            { "metric.gpuVram", "VRAM" }, { "metric.gpuFan", "GPU-Lüfter" }, { "metric.ramLoad", "RAM" }, { "metric.cpuCores", "CPU-Kerne (Diagramm)" },
            { "metric.clock", "Uhr" }, { "metric.date", "Datum" }, { "metric.text", "Text" },
            { "color.palette", "Palette" }, { "color.custom", "Eigene Farbe" }, { "color.recent", "Letzte Farbe" },
            { "color.white", "Weiß" }, { "color.cyan", "Cyan" }, { "color.green", "Grün" },
            { "color.amber", "Bernstein" }, { "color.red", "Rot" }, { "color.blue", "Blau" },
            { "text.default", "Text" }, { "layer.textPrefix", "Text: " },
            { "preview.hint", "Elemente ziehen zum Verschieben; Hintergrund ziehen zum Positionieren." },
            { "btn.start", "Streaming starten" }, { "btn.stop", "Stopp" },
            { "st.ready", "Bereit" }, { "st.starting", "Streaming wird gestartet..." },
            { "st.stopped", "Streaming gestoppt" }, { "st.active", "Streaming aktiv" },
            { "st.freed", "Geräte freigegeben" }, { "st.freeing", "Originalsoftware wird geschlossen..." },
            { "st.freeFail", "Myth.Cool / TRCC kann nicht geschlossen werden" },
            { "msg.noTarget", "Aktiviere mindestens einen Bildschirm über das Kontrollkästchen unter dem Tab." },
            { "dlg.pickGif", "GIF auswählen" }, { "dlg.pickImage", "Bild auswählen" },
            { "dlg.images", "Bilder" },
            { "prof.save", "Speichern" }, { "prof.unsaved", "Ungespeicherte Aenderungen" },
            { "prof.deleteTitle", "Profil loeschen" }, { "prof.deleteConfirm", "Profil \"{0}\" loeschen?" },
            { "err.image", "Ungültiges Bild: " }, { "err.gif", "Ungültige GIF: " },
            { "prof.label", "Profil" }, { "prof.load", "Laden" }, { "prof.saveAs", "Speichern unter…" },
            { "prof.delete", "Löschen" }, { "prof.autostart", "Mit Windows starten" }, { "prof.none", "(kein Profil)" },
            { "prof.nameTitle", "Profilname" }, { "prof.saved", "Profil gespeichert" }, { "prof.loaded", "Profil geladen" },
            { "prof.deleted", "Profil gelöscht" }, { "dlg.ok", "OK" }, { "dlg.cancel", "Abbrechen" },
            { "tray.open", "Öffnen" }, { "tray.exit", "Beenden" },
            { "tray.hint", "LCD Fusion läuft im Infobereich weiter: Streaming wird fortgesetzt." },
            { "lang.label", "Sprache" },
            { "btn.about", "Info" }, { "dlg.close", "Schliessen" },
            { "about.title", "Info und Rechtliches" }, { "about.tab.project", "Projekt" }, { "about.tab.legal", "Rechtliches" }, { "about.tab.licenses", "Lizenzen" },
            { "about.subtitle", "Open-Source-LCD-Controller fuer Valkyrie- und Thermalright-Displays." },
            { "about.version", "Version" }, { "about.developer", "Entwickler" }, { "about.license", "Lizenz" }, { "about.repository", "Repository" },
            { "about.opensource", "Der Quellcode steht unter der MIT-Lizenz. Drittkomponenten behalten ihre eigenen Lizenzen." },
            { "about.vendorDisclaimer", "Inoffizielles Projekt: nicht mit Valkyrie, Thermalright, Myth.Cool oder TRCC verbunden, unterstuetzt oder gesponsert." },
            { "legal.unofficial", "LCD Fusion ist ein inoffizielles Community-Projekt. Herstellernamen beschreiben nur die Hardware-Kompatibilitaet." },
            { "legal.interop", "Interoperabilitaet: Das Protokoll wurde durch Beobachtung von USB-Verkehr eigener Geraete rekonstruiert, ohne Code, Firmware oder Assets der Hersteller zu kopieren." },
            { "legal.trademarks", "Marken: Valkyrie, Thermalright, Myth.Cool und TRCC gehoeren ihren jeweiligen Eigentuemerinnen und Eigentuemern." },
            { "legal.noTelemetry", "Datenschutz: Die App nutzt keine Telemetrie und fuehrt keine Netzwerkaufrufe aus." },
            { "legal.risk", "Nutzung auf eigenes Risiko: Direktes Ansteuern des LCDs kann unerwartete Auswirkungen auf Geraet oder Garantie haben." },
            { "legal.fileMissing", "Legal-Datei im App-Ordner nicht gefunden." },
        };

        private static readonly Dictionary<string, Dictionary<string, string>> _all =
            new Dictionary<string, Dictionary<string, string>> { { "it", It }, { "en", En }, { "de", De } };
    }
}
