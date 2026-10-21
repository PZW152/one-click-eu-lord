// What Windows shows about this file.
//
// Right-click the exe, Properties, Details: before these attributes existed that
// page was blank and the file version read 0.0.0.0. On a program with no
// code-signing certificate that matters more than it sounds -- SmartScreen will
// warn about it either way, and an executable that also refuses to say what it is
// or where it came from gives someone every reason to stop there.
//
// The version here is checked against Build.Version, app.manifest and the
// CHANGELOG by tools\Check-Version.ps1, so all four move together or CI fails.

using System;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("One-Click EU Lord")]
[assembly: AssemblyProduct("一鍵轉生歐爺 · 一键转生欧爷 · One-Click EU Lord")]
[assembly: AssemblyDescription(
    "Gives Windows 11 every gain from Ireland's EU/EEA (DMA) policy without the features the EU build removes, and keeps it across updates.")]

// An independent open-source project. Not Microsoft, and the file properties
// should never leave room to think otherwise.
[assembly: AssemblyCompany("PZW152 — independent open-source project, not affiliated with Microsoft")]
[assembly: AssemblyCopyright("MIT licence. https://github.com/PZW152/one-click-eu-lord")]

[assembly: AssemblyVersion("2.0.1.0")]
[assembly: AssemblyFileVersion("2.0.1.0")]
[assembly: AssemblyInformationalVersion("2.0.1")]

[assembly: ComVisible(false)]
[assembly: CLSCompliant(false)]
