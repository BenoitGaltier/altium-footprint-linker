# Footprint Linker

**English** | [Français](README.fr.md)

A portable Windows application that adds **additional footprints** to Altium 365 components using a CSV file and a directory of `.PcbLib` files.

Built on the same architecture as [Component Lens](https://github.com/BenoitGaltier/altium-component-lens): a self-contained executable, a local browser interface, and workspace URLs and API tokens saved with Windows encryption.

## Getting started

Download the ZIP from the [latest release](https://github.com/BenoitGaltier/altium-footprint-linker/releases/latest), extract it, and run **FootprintLinker.exe**. Windows 10/11 x64 and a modern browser are sufficient. No .NET SDK, Node.js, or Altium Designer installation is required. Keep the console window open while using the application.

The interface currently uses French labels. The steps below include the exact button names.

1. Connect to your workspace using its URL and an API token allowed to read components, import footprints, and update footprint associations.
2. Select a CSV file containing the six columns shown below. Column order may vary; additional columns are accepted and ignored.
3. Configure the **Altium field matching Item** (`AU_ID` by default). Click **Parcourir…** (Browse) to select the root directory containing your `.PcbLib` files with the native Windows folder picker. You can also enter the path manually.
4. Click **1. Analyser les composants** (Analyze components). The table lists matching components, local footprints and their relative paths, and the inferred destination folders.
5. Select the associations you want to add, then click **2. Ajouter aux composants sélectionnés** (Add to selected components). No rows are selected automatically. The confirmation dialog summarizes the workspace and the number of associations.
6. Export the JSON report to retain the matches, results, and operation journal.

Each recognized local footprint has a **Voir 2D / 3D** (View 2D / 3D) button. Previews are generated locally, directly from the `.PcbLib`, without launching Altium or sending the file to a visualization service.

## 2D and 3D previews

- **2D view:** all populated layers on both sides are visible by default, with a distinct color and checkbox for each layer. The **Couches 2D** (2D layers) panel supports multiple selections, **Toutes** (All), **Aucune** (None), and **Seule** (Only) to isolate one layer. Use the mouse wheel to zoom, drag to pan, and **Recentrer** (Fit view) to restore the initial view. **Exporter SVG** preserves the current layer selection.
- Copper, Overlay, Paste, Solder Mask, mechanical, and Keep-Out layers are shown separately. Pad paste and solder mask openings are projected onto the correct side, respecting manual expansions and tenting. Openings controlled by unavailable board rules use nominal pad dimensions and are flagged. Drill holes and 3D body positions have separate groups. Empty layers are not listed.
- **3D view:** pads, tracks, holes, and embedded STEP models are converted locally into meshes. Drag to rotate, right-drag to pan, and use the mouse wheel to zoom. WebGL 2 is required. Extruded bodies defined by an outline and heights are also displayed.
- The preview uses the internal footprint name requested in the CSV, including when a library contains multiple footprints. The file hash is checked again before rendering, as it is before import.
- Missing external models, unsupported bodies, and conversion failures are reported. If no usable 3D body exists, the 3D view displays the available footprint primitives with a warning. Package geometry is never invented from the component name.
- **Exporter GLB** exports the converted 3D scene. It is disabled when the viewer has added extruded bodies that are not included in the GLB file.

Previews support visual inspection; they do not qualify manufacturing dimensions. Holes use a conventional support thickness, and external textures and some body types are unsupported. Rendering uses AltiumSharp/OriginalCircuit and Three.js bundled in the executable, without Altium Designer.

## CSV format

```csv
Item;Description;Supplier;Supplier Name;Supplier Item No;footprint
00042;Example component;SUP;Example supplier;REF-001;SOT-23
```

A [sample CSV](examples/footprints.csv) is included and can also be downloaded from the application. Supported formats include comma, semicolon, tab, and `|` separators; quoted fields; multiline fields; UTF-8 with or without a BOM; and Windows-1252. Limits are 10 MB and 50,000 rows. Malformed quoting and inconsistent column counts are rejected.

## Matching and import behavior

- `Item` is compared with the **configurable Altium field** (`AU_ID` by default) after trimming outer whitespace. Values retain their case, punctuation, and leading zeros: `00042` is different from `42`. The field name is case-insensitive. This setting is saved in encrypted local storage; changing it invalidates the previous analysis.
- Revision parameters take precedence over inherited item parameters, including when the revision value is empty. Conflicting values are not used as an identifier.
- The inventory reads every accessible library page and uses the **latest revision** of each component. One value can match multiple components; all are listed, with one row per association. Matched components remain visible even if their local footprint is missing or their destination is ambiguous.
- Repeated `Item / footprint` pairs in the CSV are flagged. Multiple different footprints for the same Item are allowed.
- The selected directory and **all subdirectories** are searched recursively, without following junctions or symbolic links. The application reads **internal footprint names** from `.PcbLib` files; the filename can be different. Footprint name matching is case-insensitive and accepts an optional `.PcbLib` suffix in the CSV. Local directory paths are not used to resolve duplicate names: if the same name appears in multiple files, the row is blocked. The chosen relative path is displayed in the table.
- A library containing multiple footprints is reduced to the selected footprint in a temporary copy. Primitives and embedded models are copied as binary data. Previous workspace bindings (`ITEMGUID`, `REVISIONGUID`, `VAULTGUID`, and `FOLDERGUID`) are neutralized without changing binary block lengths. Source files remain unchanged. Embedded 3D models are preserved; external dependencies absent from the `.PcbLib` are not collected.
- The **Altium destination is inferred separately for each component**: first from the default footprint folder, otherwise from the common folder of its other existing footprints. If no footprint folder is available, the component hierarchy is compared with `PCB_COMPONENT_LIBRARY` folders using the longest common path suffix. A unique match is required; missing or ambiguous destinations block the row. The table shows the destination path and the reason it was selected. This Altium hierarchy is used only for the remote destination, not to resolve local filename ambiguity.
- The inferred folder's naming scheme is used when available, including a scheme inherited from a parent. Otherwise, the internal footprint name is proposed as the Altium name. Each workspace/folder/internal-name/SHA-256 combination is imported once. Components with different destinations require separate imports. The destination and matching field are checked again before writing.
- Existing associations are skipped. `desAddFootprintToComponent` is called without `addAtIndex`, so the new footprint is **appended to the list**. Existing associations and the default footprint are verified after the write.
- `Description`, `Supplier`, `Supplier Name`, and `Supplier Item No` support review and remain in the report; they do not modify component parameters. The operation does not create a new component revision.

Incomplete analysis, partially unreadable directories, ambiguous files, and changed files or components are reported. Coverage warnings block import. Altium's reported total does not limit pagination; a total below the returned count is accepted only after a second independent traversal returns identical results, as in Component Lens.

## Recovery after interruption

Every creation and association is journaled **before** the remote write. Mutations are never automatically repeated after a network error. Read operations may be retried after temporary failures.

A creation with an uncertain outcome blocks reimporting the same file. Under **Données locales et journal de reprise** (Local data and recovery journal), enter the operation key and footprint ID after checking directly in Altium. The application verifies the supplied ID's name/comment and folder. If you have confirmed that no footprint was created, enter `ABSENT` to allow another attempt. For an uncertain association, its presence is checked again before any action.

**Arrêter** (Stop) cancels analysis or stops import between rows. A write already in progress continues through verification. Terminating the process or losing the network can leave a footprint created but not associated; its ID remains in the journal if the response was received. Operations are not transactional, and there is no automatic remote deletion or rollback.

## URLs, tokens, and local data

The same URL formats as Component Lens are accepted: `workspace.365.altium.com`, its HTTPS URL, or its `/api/graphql` endpoint, on `.altium.com` and `.altium365.com` domains. Tokens pasted with a `Bearer` prefix are accepted.

The connection, directory, matching field, and journal are saved in **`%LOCALAPPDATA%\FootprintLinker\state.dat`**, encrypted with Windows DPAPI for the current user. Writes are atomic, with an encrypted `.bak` backup and an exclusive lock. Corrupted files are preserved and reported. A saved token can be reused by leaving its field empty, only for its original URL. Restarting restores settings without contacting Altium; click **Connecter** (Connect) to check the connection and load folders. Version 1.0.0 state remains compatible: connections and the journal are preserved, the matching field defaults to `AU_ID`, and the former global destination is ignored.

Tokens are never returned by the local API or included in reports, URLs, logs, or browser storage. The server listens only on `127.0.0.1`, on a random port, with a per-process session key, Host/Origin validation, and a Content Security Policy. No CDN or telemetry is used. HTTP redirects are disabled for authenticated requests.

**Effacer la connexion et le journal** (Clear connection and journal) also deletes the local backup; Altium data remains in place. Export the journal before clearing it: import deduplication relies on this journal. Footprints created by another tool are not reused based only on their name. Detailed analysis/import results remain in memory until the application closes and can be exported; the journal persists across launches.

## Development

Development requirements: .NET SDK 10, Node.js 22+, and Git. Rendering dependencies are pinned in Git submodules and `package-lock.json`, and bundled in the portable ZIP. Import reading/extraction uses Windows Structured Storage. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for licenses.

```powershell
git -c core.longpaths=true clone --recurse-submodules https://github.com/BenoitGaltier/altium-footprint-linker.git
cd altium-footprint-linker
npm ci
npm run vendor
./scripts/test.ps1
dotnet run --project src/FootprintLinker
./scripts/publish.ps1
./scripts/smoke.ps1
```

For an existing checkout, run `git -c core.longpaths=true submodule update --init --recursive`, then `npm ci`. The Source Link tooling supplied by the .NET 10 SDK replaces the upstream project's obsolete explicit reference; dependency auditing remains enabled.

Local preview export also works without a workspace token:

```powershell
./FootprintLinker.exe --render-preview C:\Libraries\package.PcbLib SOT-23 C:\Previews\SOT23
```

This command produces `preview.svg`, `preview.glb` when 3D conversion succeeds, and metadata in `preview.json`.

Options: `--headless`, `--port 4178`, `--data-dir PATH`, and `--no-persistence`. The last option allows analysis but blocks imports because they require a durable journal. Tests use isolated directories and a simulated Altium server; they neither contact nor modify a real workspace.

The ZIP and its SHA-256 checksum are generated in `dist/`. Tags matching `v*` trigger tests, the self-contained build, binary smoke checks, and publication of a GitHub release. The binary is unsigned; the bundled runtime may extract native files into the Windows temporary directory on first launch.

See [validation coverage and limitations](docs/VALIDATION.md) (French). A test import into a test workspace is still required to validate permissions, naming schemes, and real files in your environment. Avoid simultaneous library changes during the operation: the API does not provide an atomic transaction between reading and appending an association.

## References

- [Altium 365 API and authentication](https://www.altium.com/documentation/altium-developer-center/altium-365/api)
- [Add a footprint to a component](https://docs.developer.altium.com/platform-api-docs/reference/library-management/operations/mutations/des-add-footprint-to-component/)
- [Append order and preserving the default footprint](https://docs.developer.altium.com/platform-api-docs/reference/library-management/types/inputs/des-add-footprint-to-component-input/)
- [Create a footprint](https://docs.developer.altium.com/platform-api-docs/reference/library-management/types/inputs/des-create-footprint-input/)
- [Windows DPAPI encryption](https://learn.microsoft.com/en-us/windows/win32/secauthn/data-protection)

An independent application based on Component Lens. This project is not an Altium product.
