using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GAIP.Core;
using GAIP.Storage;
using GAIP.Sync;

namespace GAIP.Desktop;

public sealed partial class MainWindow
{
    private async Task Configure()
    {
        var form = new FormWindow("Configuration", width: 760);
        var mode = new ComboBox { Name = "StorageMode", ItemsSource = new[] { "Personnel / Local", "Réseau / Partagé" }, SelectedIndex = (int)_config.Mode };
        var path = Ui.Input(_config.SharedPath, "Chemin absolu du dossier partagé", 2000); path.Name = "SharedPath";
        var interval = new NumericUpDown { Name = "SyncSeconds", Minimum = 5, Maximum = 86400, Value = _config.SyncSeconds, FormatString = "0" };
        var backups = new NumericUpDown { Name = "BackupCount", Minimum = 1, Maximum = 10000, Value = _config.BackupCount, FormatString = "0" };
        var separator = Ui.Input(_config.CsvSeparator, ";", 1); separator.Name = "CsvSeparator";
        var theme = new ComboBox { Name = "Theme", ItemsSource = new[] { "Système", "Clair", "Sombre" }, SelectedIndex = (int)_config.Theme };
        var homeColumns = new NumericUpDown { Name = "MaxHomeColumns", Minimum = 1, Maximum = 8, Value = _config.MaxHomeColumns, FormatString = "0" };
        var multicastTileMax = MaxMulticastHomeTiles(Db.MulticastGroups.Count);
        var multicastTiles = new NumericUpDown
        {
            Name = "MulticastHomeTiles", Minimum = 1, Maximum = multicastTileMax,
            Value = Math.Min(_config.MulticastHomeTiles, multicastTileMax), FormatString = "0",
            IsEnabled = Db.MulticastGroups.Count >= 10
        };
        var migration = new ComboBox { Name = "SharedMigration", ItemsSource = new[] { "Utiliser uniquement une base réseau existante", "Initialiser depuis la base actuelle si aucune base réseau n’existe" }, SelectedIndex = 0 };
        var localChoice = new ComboBox { Name = "LocalMigration", ItemsSource = new[] { "Copier la base réseau / cache actuel", "Créer une base locale vide" }, SelectedIndex = 0 };

        AppConfig ReadConfig() => new()
        {
            Mode = (StorageMode)mode.SelectedIndex, SharedPath = path.Text?.Trim() ?? "",
            SyncSeconds = (int)(interval.Value ?? 60), BackupCount = (int)(backups.Value ?? 30),
            CsvSeparator = separator.Text ?? ";", Theme = (AppTheme)theme.SelectedIndex,
            MaxHomeColumns = (int)(homeColumns.Value ?? 3),
            MulticastHomeTiles = (int)(multicastTiles.Value ?? 1)
        };

        var storage = Ui.Column(
            Ui.Field("Mode", mode),
            Ui.Field("Dossier partagé", path),
            Ui.Button("Tester l’accès", async () =>
            {
                try
                {
                    var repository = new FileRepository(path.Text ?? "", UserPaths.User, UserPaths.Machine);
                    await Io(repository.TestAccess); form.Error.Text = "Accès en lecture et écriture vérifié.";
                }
                catch (Exception ex) { form.Error.Text = ex.Message; }
            }),
            Ui.Field("Lors du passage vers un partage", migration),
            Ui.Field("Lors du passage du partagé vers le local", localChoice),
            Ui.Field("Synchronisation (secondes)", interval),
            Ui.Field("Sauvegardes conservées", backups),
            Ui.Text($"Données locales : {_localRoot}\nConfiguration : {_configRoot}", 11),
            Ui.Button("Diagnostic / gestion du verrou", () => Run(Diagnostics)));

        var siteOrder = new SiteOrderEditor(Db.Sites) { IsEnabled = CanWrite };
        var display = Ui.Column(
            Ui.Field("Thème", theme),
            Ui.Field("Colonnes maximum sur l’accueil", homeColumns),
            Ui.Text("Le nombre de colonnes s’adapte automatiquement à la largeur disponible sans dépasser cette limite.", 12),
            Ui.Field("Tuiles multicast sur l’accueil", multicastTiles),
            Ui.Text(Db.MulticastGroups.Count == 0
                ? "Aucun groupe multicast : aucune tuile multicast n’est affichée."
                : $"Maximum actuel : {multicastTileMax} tuile(s), afin de conserver environ 5 groupes minimum par tuile.", 12),
            Ui.Text("Ordre d’affichage des sites", 16, true),
            Ui.Text("Classez les sites par glisser-déposer. L’ordre est enregistré avec les autres réglages de cet onglet.", 12),
            siteOrder,
            Ui.Text(CanWrite ? "Le verrou partagé sera pris uniquement si l’ordre des sites a changé." :
                "Lecture seule : le stockage partagé est actuellement hors ligne.", 12));

        var exportButton = Ui.Button("Exporter la configuration…", async () =>
        {
            try
            {
                var exported = ReadConfig(); exported.Validate();
                var file = await form.StorageProvider.SaveFilePickerAsync(new()
                {
                    Title = "Exporter la configuration",
                    SuggestedFileName = "GAIP-config.json",
                    DefaultExtension = "json",
                    FileTypeChoices = [JsonType],
                    ShowOverwritePrompt = true
                });
                if (file is null) return;
                await using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
                await JsonSerializer.SerializeAsync(stream, exported, StorageJsonContext.Default.AppConfig);
                await stream.FlushAsync();
                form.Error.Text = "Configuration exportée.";
            }
            catch (Exception ex) { form.Error.Text = ex.Message; }
        });
        var data = Ui.Column(Ui.Field("Séparateur CSV", separator), exportButton);

        var tabs = new TabControl
        {
            Name = "ConfigurationTabs",
            ItemsSource = new[]
            {
                new TabItem { Header = "Stockage & synchronisation", Content = storage },
                new TabItem { Header = "Affichage", Content = display },
                new TabItem { Header = "Données & export", Content = data }
            },
            SelectedIndex = 0
        };
        tabs.SelectionChanged += (_, _) => form.Save.Content = tabs.SelectedIndex == 1 ? "Enregistrer l’affichage" : "Enregistrer";
        form.Fields.Children.Add(tabs);

        form.Submit = async () =>
        {
            var next = ReadConfig();
            next.Validate();
            var allowInitialize = migration.SelectedIndex == 1;
            var emptyLocal = localChoice.SelectedIndex == 1;
            var source = _session?.HasData == true ? JsonData.Clone(Db) : null;
            bool storageChanged = next.Mode != _config.Mode || (next.Mode == StorageMode.Shared && next.SharedPath != _config.SharedPath);
            if (next.Mode == StorageMode.Local && _config.Mode == StorageMode.Shared)
            {
                if (!emptyLocal && source is null) throw new InvalidOperationException("Aucune base ni cache valide à copier.");
                if (!await Confirm("Créer la base locale", "La base locale sera remplacée par votre choix. Une base locale existante sera sauvegardée avant remplacement."))
                    throw new InvalidOperationException("Changement annulé.");
            }
            var opened = await Io(() =>
            {
                if (storageChanged && next.Mode == StorageMode.Shared)
                {
                    var repository = new FileRepository(next.SharedPath, UserPaths.User, UserPaths.Machine, next.BackupCount);
                    if (!File.Exists(repository.DataPath))
                    {
                        if (!allowInitialize) throw new IOException("Aucune base réseau. Choisissez explicitement son initialisation, ou un autre dossier.");
                        repository.Initialize(source ?? new());
                    }
                }
                if (next.Mode == StorageMode.Local && _config.Mode == StorageMode.Shared)
                {
                    var root = Path.Combine(_localRoot, "local"); Directory.CreateDirectory(root);
                    var repository = new FileRepository(root, UserPaths.User, UserPaths.Machine, next.BackupCount);
                    var seed = emptyLocal ? new Database() : source!;
                    if (File.Exists(repository.DataPath))
                        repository.Commit(seed, repository.Read().Hash, null, "Passage en local", "Base", "Locale");
                    else repository.Initialize(seed);
                }
                if (_session?.Lease is not null) _session.EndEdit();
                var session = new DataSession(next, _localRoot, UserPaths.User, UserPaths.Machine); session.Open();
                UserPaths.SaveConfig(_configRoot, next);
                return session;
            });
            _config = next; _session = opened; _selectedVlan = null; _selectedSite = null; _selectedMulticast = null; ApplyTheme(); Render();

            if (tabs.SelectedIndex == 1 && siteOrder.HasChanges)
                await Save(db => SiteOrdering.Apply(db, siteOrder.OrderedIds), "Ordre d’affichage", "Sites", "Accueil");
        };
        await form.ShowDialog<bool>(this);
    }

    private async Task Diagnostics()
    {
        if (_session is null) { await Message("Diagnostic", "Aucune session chargée."); return; }
        var form = new FormWindow("Synchronisation et verrou", "Fermer", 740);
        form.Fields.Children.Add(Ui.Text($"État : {_session.Status}\nRévision : {Db.Revision}\nSHA-256 : {_session.Hash}\nStockage : {_session.Repository.Root}\nDernière modification : {Db.LastModified.LocalDateTime:g}\nPar : {Db.LastModifiedBy} / {Db.LastModifiedFrom}"));
        if (_session.Config.Mode == StorageMode.Shared)
        {
            form.Fields.Children.Add(Ui.Text("Récupération : restaure la base et l’historique depuis le cache local validé. Refus si gaip-data.json, history.jsonl ou edit.lock existe. Les sauvegardes sont conservées ; une sauvegarde plus récente que le cache bloque l’opération.", 12));
            form.Fields.Children.Add(Ui.Button("Restaurer la base et l’historique depuis le cache…", async () =>
            {
                try
                {
                    if (!await Confirm("Restaurer depuis le cache",
                        $"Restaurer la base et l’historique du dernier cache local validé vers {_session.Repository.Root} ?\n\n" +
                        "Aucun fichier existant ne sera écrasé. La restauration est refusée si gaip-data.json, history.jsonl ou edit.lock existe. " +
                        "Les sauvegardes présentes sont conservées et servent de garde-fou contre un cache trop ancien.")) return;
                    var result = await Io(_session.RestoreSharedFromCache);
                    Render();
                    var warning = _session.Warning;
                    form.Close(true);
                    var message = $"Base et historique restaurés depuis le cache local validé.\nRévision : {result.Snapshot.Data.Revision}\nHistorique : {result.HistoryEntries} entrée(s).\nLes sauvegardes existantes ont été conservées.";
                    await Message("Restauration terminée", warning is null ? message : message + "\n" + warning);
                }
                catch (Exception ex) { form.Error.Text = ex.Message; }
            }));
        }
        EditLease? lease = null;
        try { lease = await Io(_session.Repository.ReadLease); }
        catch (Exception ex) { form.Fields.Children.Add(Ui.Text(ex.Message)); }
        if (lease is not null)
        {
            form.Fields.Children.Add(Ui.Text($"Verrou : {lease.User} / {lease.Machine}\nAcquisition : {lease.AcquiredAt.LocalDateTime:G}\nDernier heartbeat : {lease.Heartbeat.LocalDateTime:G}\nRévision de départ : {lease.StartRevision}"));
            form.Fields.Children.Add(Ui.Button("Forcer la libération du verrou…", async () =>
            {
                try
                {
                    if (!await Confirm("Forcer la libération", $"Retirer le verrou de {lease.User} / {lease.Machine} ? Cette session perdra le droit de publier. L’action sera inscrite dans l’historique.", true)) return;
                    await Io(() => _session.Repository.ForceRelease(lease.Id)); await Refresh(); form.Close(true);
                }
                catch (Exception ex) { form.Error.Text = ex.Message; }
            }));
        }
        else form.Fields.Children.Add(Ui.Text("Aucun verrou présent."));
        await form.ShowDialog<bool>(this);
    }

    private async Task History(Guid? vlanId = null, string? multicastAddress = null)
    {
        if (_session is null) return;
        var lines = await Io(() => multicastAddress is not null
            ? _session.Repository.History(multicastAddress)
            : _session.Repository.History(vlanId));
        var title = multicastAddress is not null
            ? $"Historique · Multicast {multicastAddress} · 1 000 dernières actions liées"
            : vlanId is null ? "Historique · 1 000 dernières actions" : $"Historique · {VlanLabel(vlanId.Value)} · 1 000 dernières actions liées";
        var form = new FormWindow(title, "Fermer", 920);
        if (lines.Count == 0) form.Fields.Children.Add(Ui.Text("Aucune action enregistrée."));
        else
        {
            var search = Ui.Input("", "Recherche sur date, révision, action, cible, utilisateur et détails", 300);
            search.Name = "HistorySearch";
            var count = Ui.Text("", 11);
            form.Fields.Children.Add(Ui.SearchField(search, "Rechercher dans l’historique"));
            form.Fields.Children.Add(count);
            var rows = new List<(Control Control, string SearchText)>();

            foreach (var line in lines)
            {
                try
                {
                    var entry = JsonSerializer.Deserialize(line, StorageJsonContext.Default.AuditEntry)!;
                    var header = $"{entry.Date.LocalDateTime:g} · r{entry.Revision} · {entry.Action} {entry.ObjectType} {entry.Target} · {entry.User} / {entry.Machine}";
                    var details = HistoryDetails(entry);
                    var detail = new Expander
                    {
                        Header = header,
                        Content = new TextBox { Text = details, IsReadOnly = true, AcceptsReturn = true, MaxHeight = 300 }
                    };
                    rows.Add((detail, $"{entry.Date:O} {header} {details}"));
                    form.Fields.Children.Add(detail);
                }
                catch (JsonException)
                {
                    var invalid = Ui.Text("Ligne d’historique incomplète ou illisible : " + line);
                    rows.Add((invalid, line));
                    form.Fields.Children.Add(invalid);
                }
            }

            void ApplyFilter()
            {
                var terms = (search.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var visible = 0;
                foreach (var row in rows)
                {
                    var match = terms.All(term => row.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase));
                    row.Control.IsVisible = match;
                    if (match) visible++;
                }
                count.Text = terms.Length == 0 ? $"{rows.Count} action(s)" : $"{visible} résultat(s) sur {rows.Count}";
            }

            search.TextChanged += (_, _) => ApplyFilter();
            ApplyFilter();
        }
        await form.ShowDialog<bool>(this);
    }

    private static string HistoryDetails(AuditEntry entry)
    {
        if (entry.Changes.Count == 0) return "Aucun changement métier détaillé.";
        var groups = entry.Changes
            .GroupBy(change => (change.ObjectType, change.Target))
            .ToList();
        var text = new StringBuilder();

        foreach (var group in groups)
        {
            if (text.Length > 0) text.AppendLine().AppendLine();
            if (groups.Count > 1)
                text.Append(group.Key.ObjectType).Append(' ').Append(group.Key.Target).AppendLine();

            var changes = group.ToList();
            for (var i = 0; i < changes.Count; i++)
            {
                if (i > 0) text.AppendLine().AppendLine();
                var change = changes[i];
                var indent = groups.Count > 1 ? "  " : "";
                text.Append(indent).Append(HistoryFieldLabel(change.Field)).AppendLine();
                text.Append(indent).Append("  ").Append(HistoryValue(change.Field, change.OldValue))
                    .Append("  →  ").Append(HistoryValue(change.Field, change.NewValue));
            }
        }
        return text.ToString();
    }

    private static string HistoryFieldLabel(string field) => field switch
    {
        "exists" => "Existence",
        "code" => "Code",
        "name" => "Nom",
        "description" => "Description",
        "displayOrder" => "Ordre d’affichage",
        "vid" => "VID",
        "cidr" => "CIDR",
        "address" => "Adresse",
        "comment" => "Commentaire",
        "hostname" => "Hostname",
        "content" => "Contenu",
        "sources" => "Sources",
        "vlans" => "VLAN",
        "vlanReference" => "Association VLAN",
        "holder" => "Détenteur",
        "startRevision" => "Révision de départ",
        "startHash" => "Hash de départ",
        "hash" => "SHA-256",
        _ => field
    };

    private static string HistoryValue(string field, string? value)
    {
        if (value is null) return "∅";
        if (field == "exists") return value == "true" ? "présent" : "absent";
        return value.Length == 0 ? "« vide »" : value;
    }

    private string VlanLabel(Guid vlanId)
    {
        var site = Db.Sites.Single(s => s.Vlans.Any(v => v.Id == vlanId));
        var vlan = site.Vlans.Single(v => v.Id == vlanId);
        return $"{site.Code} / VLAN {vlan.Vid} — {vlan.Name}";
    }
    private async Task CsvDialog(Guid? vlanId = null)
    {
        var form = new FormWindow(vlanId is null ? "Import / export CSV et Excel" : $"Export · {VlanLabel(vlanId.Value)}", "Fermer", 720);
        if (vlanId is null)
        {
            form.Fields.Children.Add(Ui.Text($"UTF-8 · séparateur « {_config.CsvSeparator} ». Importez les VLAN, puis les adresses, puis les multicast. Les lignes existantes sont mises à jour ; aucune ligne absente du fichier n’est supprimée."));
            form.Fields.Children.Add(Ui.Button("Importer vlans.csv…", () => Run(() => ImportCsv(CsvKind.Vlans)), CanWrite));
            form.Fields.Children.Add(Ui.Button("Importer addresses.csv…", () => Run(() => ImportCsv(CsvKind.Addresses)), CanWrite));
            form.Fields.Children.Add(Ui.Button("Importer multicast.csv…", () => Run(() => ImportCsv(CsvKind.Multicast)), CanWrite));
        }
        else form.Fields.Children.Add(Ui.Text("Les exports CSV contiennent uniquement ce VLAN. L’export Excel produit toujours le classeur complet."));
        form.Fields.Children.Add(Ui.Button(vlanId is null ? "Exporter les VLAN / réseaux…" : "Exporter ce VLAN / réseau…", () => Run(() => ExportCsv(CsvKind.Vlans, vlanId))));
        form.Fields.Children.Add(Ui.Button("Exporter les adresses IP…", () => Run(() => ExportCsv(CsvKind.Addresses, vlanId))));
        if (vlanId is null)
            form.Fields.Children.Add(Ui.Button("Exporter les multicast…", () => Run(() => ExportCsv(CsvKind.Multicast))));
        form.Fields.Children.Add(Ui.Button(vlanId is null ? "Exporter les trois fichiers CSV…" : "Exporter les deux fichiers CSV…", () => Run(() => ExportAll(vlanId))));
        form.Fields.Children.Add(Ui.Button("Exporter le classeur Excel complet…", () => Run(ExportExcel)));
        form.Fields.Children.Add(Ui.Text("Excel : le premier onglet liste les sites et VLAN, l’onglet Multicast reprend les groupes/flux et leur matrice de sites, puis chaque réseau possède son onglet avec toutes les adresses IP utilisables.", 12));
        form.Fields.Children.Add(Ui.Text("Les passerelles figurent dans vlans.csv uniquement.", 12));
        await form.ShowDialog<bool>(this);
    }
    private static FilePickerFileType CsvType => new("CSV UTF-8") { Patterns = ["*.csv"] };
    private static FilePickerFileType ExcelType => new("Classeur Excel") { Patterns = ["*.xlsx"] };
    private static FilePickerFileType JsonType => new("Configuration JSON") { Patterns = ["*.json"] };
    private async Task ImportCsv(CsvKind kind)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Importer CSV", AllowMultiple = false, FileTypeFilter = [CsvType] });
        if (files.Count == 0) return;
        string text;
        await using (var stream = await files[0].OpenReadAsync())
        using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true)) text = await reader.ReadToEndAsync();
        var result = CsvExchange.Import(Db, text, kind, _config.CsvSeparator[0]);
        var preview = new FormWindow("Vérification de l’import", "Publier l’import", 850);
        preview.Fields.Children.Add(Ui.Text($"{result.RowCount} enregistrement(s) · {result.Errors.Count} erreur(s)", 18, true));
        if (result.Errors.Count > 0)
        {
            preview.Fields.Children.Add(new TextBox { Text = string.Join("\n", result.Errors), IsReadOnly = true, AcceptsReturn = true, MinHeight = 300 });
            preview.Save.IsEnabled = false;
        }
        else
        {
            preview.Fields.Children.Add(Ui.Text("Validation réussie. Le fichier sera importé en une seule publication avec sauvegarde préalable."));
            var separator = _config.CsvSeparator[0];
            preview.Submit = () => Save(db =>
            {
                // Revalidate against the latest session, not a stale preview.
                var current = CsvExchange.Import(db, text, kind, separator);
                if (current.Data is null) throw new ValidationException(current.Errors);
                if (kind == CsvKind.Multicast) db.MulticastGroups = current.Data.MulticastGroups;
                else db.Sites = current.Data.Sites;
            }, "Import CSV", kind switch
            {
                CsvKind.Vlans => "VLAN",
                CsvKind.Addresses => "IP",
                CsvKind.Multicast => "Multicast",
                _ => "CSV"
            }, files[0].Name);
        }
        await preview.ShowDialog<bool>(this);
    }
    private async Task ExportCsv(CsvKind kind, Guid? vlanId = null)
    {
        var text = CsvExchange.Export(Db, kind, _config.CsvSeparator[0], vlanId);
        var file = await StorageProvider.SaveFilePickerAsync(new()
        { Title = "Exporter CSV", SuggestedFileName = kind switch { CsvKind.Vlans => "vlans.csv", CsvKind.Addresses => "addresses.csv", CsvKind.Multicast => "multicast.csv", _ => "export.csv" }, DefaultExtension = "csv", FileTypeChoices = [CsvType], ShowOverwritePrompt = true });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true)); await writer.WriteAsync(text);
    }
    private async Task ExportExcel()
    {
        var snapshot = JsonData.Clone(Db);
        var file = await StorageProvider.SaveFilePickerAsync(new()
        {
            Title = "Exporter Excel",
            SuggestedFileName = "GAIP.xlsx",
            DefaultExtension = "xlsx",
            FileTypeChoices = [ExcelType],
            ShowOverwritePrompt = true
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await Task.Run(() => ExcelExchange.Export(snapshot, stream));
        await stream.FlushAsync();
        await Message("Export terminé", "Le classeur Excel a été exporté.");
    }

    private async Task ExportAll(Guid? vlanId = null)
    {
        var kinds = vlanId is null
            ? new[] { CsvKind.Vlans, CsvKind.Addresses, CsvKind.Multicast }
            : new[] { CsvKind.Vlans, CsvKind.Addresses };
        var exports = kinds.ToDictionary(kind => kind, kind => CsvExchange.Export(Db, kind, _config.CsvSeparator[0], vlanId));
        var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = vlanId is null ? "Dossier d’export complet" : "Dossier d’export du VLAN", AllowMultiple = false });
        if (folders.Count == 0) return;
        var folder = folders[0];
        var names = new List<string>();
        await foreach (var item in folder.GetItemsAsync()) names.Add(item.Name);
        var expectedNames = kinds.Select(CsvFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Any(expectedNames.Contains) && !await Confirm("Remplacer les CSV", $"Les fichiers {string.Join(", ", expectedNames)} existants seront remplacés. Continuer ?")) return;
        foreach (var kind in kinds)
        {
            var file = await folder.CreateFileAsync(CsvFileName(kind)) ?? throw new IOException("Impossible de créer le fichier CSV.");
            await using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
            await writer.WriteAsync(exports[kind]);
        }
        await Message("Export terminé", $"{string.Join(", ", expectedNames)} ont été exportés.");

        static string CsvFileName(CsvKind kind) => kind switch
        {
            CsvKind.Vlans => "vlans.csv",
            CsvKind.Addresses => "addresses.csv",
            CsvKind.Multicast => "multicast.csv",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
