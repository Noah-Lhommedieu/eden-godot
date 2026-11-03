# FIX E-particules
Cause : NativeFxHost.BuildGeomMaterials (mode OnlyAnimatedMaterials, modèles de jeu) ne reprenait que les géométries ciblées par un contrôleur. Les géométries au NiAlphaProperty additif (flags 0xD/0x100D : blend, src=SRC_ALPHA, dst=ONE) sans contrôleur restaient au matériau NifMaterials.For (alpha-mix AlphaDepthPrePass, native/NifMaterial.cs:212) : fond noir de la texture opaque -> rectangles/halos pleins. r253 : ~25 géométries additives (Plane/Cylinder/Object/eye light).
Fix : native/NativeFxHost.cs, BuildGeomMaterials : `additive = BlendMode==3` => matériau NativeFx (unshaded, blend_add) aussi sans contrôleur.
Preuve : artifacts/shots/kit-before.png vs kit-after.png (--ride-key=253020, S001) : halos blancs opaques en bout de queues remplacés par traînées additives. Build 0 erreur.
Non vérifié : centaure thunder (id inconnu), sens de mouvement interne des particules/axes (non examiné), NifMaterial.cs hors périmètre (blend non additif toujours en alpha mix).
