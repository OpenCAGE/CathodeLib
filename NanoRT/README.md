# NanoRT.NET

A C# port of [NanoRT](https://github.com/lighttransport/nanort), Light Transport Entertainment's
single-header modern ray tracing kernel, for use in CathodeLib.

NanoRT is a binned-SAH BVH builder plus a Möller–Trumbore triangle intersector. This port keeps the
same structure (`BVHBuildOptions` → `BVHAccel.Build` → `Traverse` / `Occluded`) but is written
against `System.Numerics` and targets netstandard2.0, so it can be `Compile Include`d into
CathodeLib the same way DotRecast is.

Differences from upstream:

* C# rather than C++ templates — geometry is always an indexed `float[]`/`int[]` triangle soup.
* Build is single-threaded but traversal is allocation-free and safe to call from many threads at
  once, which is what the radiosity baker needs.
* Only the triangle mesh primitive is ported; upstream's sphere/curve/particle intersectors and
  the embedded OBJ loader are omitted.

Used by `CathodeLib.Radiosity` to trace probe visibility during the lighting bake.

Licensed MIT — see `LICENSE.txt`.
