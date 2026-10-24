"""Shared Blender-side helpers for Monolith's scripted art (see art_src/PIPELINE.md).

    mntex      numpy texture toolkit: tileable noise, painting layers, trim-sheet atlas packer, PNG writer
    mnmesh     MeshBuilder (plain arrays, explicit normals and UVs), surface patches, hard-surface primitives
    mnkit      the part kit: armour plating, window rows, running lights, turrets, nozzles, radiators ...
    mnship     Ship: LOD builders, named subsystems, collision, FBX export and the manifest Unity reads
    mnpreview  Workbench previews for checking geometry without Unity

Everything here runs inside Blender's bundled Python (tools/blender.ps1); only numpy and the standard library are used.
"""
