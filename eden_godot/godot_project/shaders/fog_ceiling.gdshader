shader_type spatial;
render_mode unshaded, cull_disabled, fog_disabled, shadows_disabled,
	ambient_light_disabled, specular_disabled,
	depth_draw_never, depth_test_disabled;

// Brouillard plafonné post-process : le fog linéaire natif Eden
// atteint FogMax (8-36 %) à FogSightRang puis ne monte plus — Godot
// ne sait pas plafonner son fog depth, donc on lit la profondeur et
// on voile l'écran en clampant à fog_max.
//
// Implémentation : MeshInstance3D QuadMesh(2×2) posé n'importe où
// dans la scène — vertex() le plaque en clip-space plein écran ;
// ALPHA=f rend la passe « transparent » (donc après l'opaque) et le
// mix(scene, fog_color, f) vient du blending — pas besoin de lire
// l'écran. hint_depth_texture est spatial-only (canvas_item ne le
// supporte pas sur Forward+ — d'où ce montage).
// Approximation documentée : rampe linéaire distance-caméra (le fog
// natif est par distance de profondeur — équivalent ici) ; les sky/
// dôme sont foggués à fog_max comme l'horizon natif.

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform vec3 fog_color : source_color = vec3(0.7, 0.8, 0.9);
uniform float fog_begin = 0.0;    // distance début du voile
uniform float fog_full = 300.0;   // distance où l'opacité = fog_max
uniform float fog_max = 0.2;      // plafond d'opacité (FogMax ini)

void vertex() {
	// Quad 2×2 → clip-space plein écran (z quelconque, test off).
	POSITION = vec4(VERTEX.xy, 0.5, 1.0);
}

void fragment() {
	float d = texture(depth_tex, SCREEN_UV).x;
	// Vulkan : NDC z ∈ [0,1] — d tel quel (pas de ×2−1).
	vec4 ndc = vec4(SCREEN_UV * 2.0 - 1.0, d, 1.0);
	vec4 view = INV_PROJECTION_MATRIX * ndc;
	float dist = -view.z / max(view.w, 1e-6);
	float f = clamp((dist - fog_begin) /
		max(fog_full - fog_begin, 0.01), 0.0, 1.0) * fog_max;
	ALBEDO = fog_color;
	ALPHA = f;
}
