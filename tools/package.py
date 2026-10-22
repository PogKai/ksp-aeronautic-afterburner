"""Build the release DLL and package AeronauticAfterburner as a KSP mod zip.

    python tools/package.py                  package the committed state
    python tools/package.py --allow-dirty    package the working tree, for a release candidate

Writes dist/AeronauticAfterburner_<version>.zip:

    GameData/AeronauticAfterburner/Plugins/AeronauticAfterburner.dll
    GameData/AeronauticAfterburner/Shaders/afterburner-{windows,macos,linux}.bundle
    GameData/AeronauticAfterburner/README.md, CHANGELOG.md, license.md, THIRD-PARTY-NOTICES.md, docs/how-it-works.md

Refuses to run when the versions in Source/AfterburnerSettings.cs, Source/AssemblyInfo.cs and the
top of CHANGELOG.md disagree, a shader bundle is missing, or (without --allow-dirty) tracked files
have uncommitted changes.
"""
import hashlib
import os
import re
import subprocess
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NAME = "AeronauticAfterburner"
DIST = os.path.join(ROOT, "dist")
BUILD = os.path.join(DIST, "build")
PLATFORMS = ("windows", "macos", "linux")
DOCS = ("README.md", "CHANGELOG.md", "license.md", "THIRD-PARTY-NOTICES.md", "docs/how-it-works.md")


def read(path):
    with open(path, encoding="utf-8-sig") as f:
        return f.read()


def run(*cmd):
    return subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True)


def main():
    allow_dirty = "--allow-dirty" in sys.argv
    found = [m and m.group(1) for m in (
        re.search(r'Version = "([^"]+)"', read(os.path.join(ROOT, "Source", "AfterburnerSettings.cs"))),
        re.search(r'AssemblyVersion\("(\d+\.\d+\.\d+)\.0"\)', read(os.path.join(ROOT, "Source", "AssemblyInfo.cs"))),
        re.search(r"^## (\S+)", read(os.path.join(ROOT, "CHANGELOG.md")), re.M))]
    if not (found[0] and found[0] == found[1] == found[2]):
        sys.exit("versions must agree: AfterburnerSettings %s, AssemblyInfo %s, top of CHANGELOG %s" % tuple(found))
    version = found[0]

    dirty = run("git", "status", "--porcelain", "--untracked-files=no").stdout.strip()
    if dirty and not allow_dirty:
        sys.exit("uncommitted changes to tracked files:\n" + dirty + "\ncommit them, or use --allow-dirty for a release candidate")

    bundles = [os.path.join(ROOT, "Shaders", "afterburner-%s.bundle" % p) for p in PLATFORMS]
    missing = [b for b in bundles if not os.path.isfile(b)]
    if missing:
        sys.exit("missing shader bundles (build them with unity/build.ps1): " + ", ".join(missing))

    print("building %s %s (Release)" % (NAME, version))
    result = run("dotnet", "build", os.path.join(ROOT, NAME + ".csproj"), "-c", "Release", "-nologo", "-v", "q", "-p:OutputPath=" + BUILD + os.sep)
    if result.returncode != 0:
        sys.exit(result.stdout + result.stderr)
    dll = os.path.join(BUILD, NAME + ".dll")

    root = "GameData/" + NAME + "/"
    files = {root + "Plugins/" + NAME + ".dll": dll}
    for b in bundles:
        files[root + "Shaders/" + os.path.basename(b)] = b
    for doc in DOCS:
        files[root + doc] = os.path.join(ROOT, *doc.split("/"))

    out = os.path.join(DIST, "%s_%s.zip" % (NAME, version))
    if os.path.exists(out):
        os.remove(out)
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for arc, path in files.items():
            info = zipfile.ZipInfo(arc, date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            with open(path, "rb") as f:
                z.writestr(info, f.read())

    with zipfile.ZipFile(out) as z, open(dll, "rb") as f:
        if z.read(root + "Plugins/" + NAME + ".dll") != f.read():
            sys.exit("the DLL in the zip is not the one just built")
        names = z.namelist()

    with open(out, "rb") as f:
        digest = hashlib.sha256(f.read()).hexdigest()
    print("wrote %s (%s bytes)" % (os.path.relpath(out, ROOT), format(os.path.getsize(out), ",")))
    print("sha256 " + digest)
    for n in names:
        print("  " + n)


if __name__ == "__main__":
    main()
