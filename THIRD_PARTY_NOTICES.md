# Third-party components

Footprint Linker bundles these components for local rendering. No Altium Designer executable, SDK or service is used for previews.

- **AltiumSharp / OriginalCircuit.Altium**, pinned at `ce72437f30cd54f549601d4e0ca5846d21272150`: https://github.com/issus/AltiumSharp. Includes OriginalCircuit.Eda.Abstractions and OriginalCircuit.Eda.Rendering at the revisions recorded by that repository. The upstream root license is reproduced in `licenses/AltiumSharp.txt` (Apache-2.0); upstream package metadata also identifies the individual projects as MIT. The upstream sources are unmodified. Build integration removes the obsolete explicit Source Link 8 package in favor of the .NET 10 SDK tooling.
- **OriginalCircuit.Mech.STEP** (`b2ea7559c4f65c18f243acb4d94beac60ee3aa4e`) and **OriginalCircuit.Mech.GLTF** (`a6984c17408d6178a42f75ad35651217107c356c`): https://github.com/issus/OriginalCircuit.Mech.STEP and https://github.com/issus/OriginalCircuit.Mech.GLTF. MIT; license copies included.
- **Three.js 0.186.1**: https://github.com/mrdoob/three, MIT. Browser module import paths are rewritten to local embedded assets; the runtime otherwise remains unmodified. License copy included.
- **OpenMcdf 3.1.4**: https://github.com/openmcdf/openmcdf/tree/11ffb7cafff6eea99639df6b2b249dcf6bcca3ef, MPL-2.0. Unmodified covered source is available at that URL. License text included.
- **SkiaSharp 3.116.1** and its Windows native assets: https://github.com/mono/SkiaSharp/tree/e57e2a11dac4ccc72bea52939dede49816842005. MIT; license and native third-party notices included.
- **Microsoft .NET 10 runtime**: https://github.com/dotnet/runtime, MIT and associated runtime notices; distributed in the self-contained executable.

The recursive Git submodules and `package-lock.json` retain the exact source/dependency versions used by this repository.
