Bundled CPython runtime bootstrap
================================

File: python.3.12.10.nupkg
Source: https://www.nuget.org/packages/python/3.12.10
Direct archive: https://api.nuget.org/v3-flatcontainer/python/3.12.10/python.3.12.10.nupkg
SHA-256: 0eb85c2dfccccf1b17352de4c397f69194035b7d37149eacc16f1147d93de3b8

This is the official 64-bit CPython 3.12.10 NuGet distribution. Qpro verifies
the archive hash, then extracts its tools/ directory into
%LOCALAPPDATA%\QproFaceTracking\runtime\python-3.12.10. It does not run the
system Python installer, register Python, change PATH, or modify an existing
Python installation. Qpro's tracking and optional ROCm packages install into
their own virtual environments.

The Python license is copied unchanged from tools/LICENSE.txt in the archive
to python-runtime/LICENSE.txt in the release ZIP.
