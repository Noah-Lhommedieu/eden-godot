shader_type spatial;
render_mode cull_back, diffuse_lambert, specular_disabled;

// Toon « anime » pour les modèles de scène (objets GLB convertis) —
// reproduction du look Eden Eternal : albedo quasi plat + ombrage
// doux à 3 paliers + rim léger. Les matériaux alpha (feuillage,
// vitraux) restent en StandardMaterial3D — voir ToonMaterializer.cs.

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec4 modulate : source_color = vec4(1.0);
uniform float rim_strength : hint_range(0.0, 1.0) = 0.12;
// Émissif natif (NiMaterialProperty::Emissive Color → emissiveFactor
// glTF). Le natif l'ajoute plat — pas modulé par la texture.
uniform vec3 emission : source_color = vec3(0.0);
uniform float emission_strength = 1.0;

void fragment() {
	// COLOR = COLOR_0 glTF = vertex colors NIF : utilisées en
	// ambiant+diffus par le natif (SourceVertexMode=AMB_DIF ou
	// propriété absente — 0 shape non-blanc en SRC_IGNORE sur les
	// objets map). Blanc (1,1,1) sur les meshes sans couleur → no-op.
	vec4 c = texture(albedo_tex, UV) * modulate * COLOR;
	ALBEDO = c.rgb;
	EMISSION += emission * emission_strength;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	RIM = rim_strength;
	RIM_TINT = 0.4;
}

void light() {
	float ndl = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
	// 3 paliers à bords adoucis : ombre 0.66 → mid 0.86 → plein 1.0.
	float t = 0.66
		+ smoothstep(0.14, 0.22, ndl) * 0.20
		+ smoothstep(0.40, 0.48, ndl) * 0.14;
	DIFFUSE_LIGHT += ALBEDO * LIGHT_COLOR.rgb * t * ATTENUATION;
}
