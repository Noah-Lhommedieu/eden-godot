shader_type spatial;
render_mode cull_disabled, specular_disabled, diffuse_lambert;

// Eau .fsm — reproduction du slot natif « waterani_*.ifl » :
//   base_tex  = TexSlot 0 (water_env.dds / magma_01.dds) dérivé
//               lentement (approximation du défilement natif) ;
//   frames    = tableau de frames du .ifl (caustNN) cyclé à `fps` ;
//   frame_count = 0 → eau statique dérivante (fallback shader).
// Blending HYPOTHESIS : le compositing exact natif (env-reflect +
// caustique additive) n'est pas désassemblé — on module la base par
// la frame caustique (éclaircit/assombrit la surface) à 55 %.

uniform vec4 tint : source_color = vec4(0.45, 0.65, 0.8, 0.62);
uniform sampler2D base_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2DArray frames : source_color, filter_linear_mipmap, repeat_enable;
uniform int frame_count = 0;
uniform float fps = 12.0;
uniform float wave = 1.0;
uniform float uv_scale = 6.0;
uniform float caustic_mix = 0.55;

void vertex() {
	// Ondulation très légère des vertices (vivacité — HYPOTHESIS).
	VERTEX.z += sin(VERTEX.x * 0.6 + TIME * 1.3)
	          * cos(VERTEX.y * 0.5 + TIME * 0.9) * 0.06 * wave;
}

void fragment() {
	vec2 uv = UV * uv_scale;
	vec2 drift = vec2(TIME * 0.013, TIME * 0.009);
	vec3 base = texture(base_tex, uv + drift).rgb;
	vec3 c = base;
	if (frame_count > 0) {
		float f = mod(floor(TIME * fps), float(frame_count));
		vec3 caust = texture(frames,
			vec3(uv * 0.6 + drift * 2.0, f)).rgb;
		// Motif caustique multiplié (les caustNN sont des taches
		// claires sur fond sombre → module la luminance).
		c = mix(base, base * (caust * 1.7), caustic_mix);
	}
	// Attr vertex #3 de l'eau = teinte + transparence par vertex
	// (mesuré : RGB ≈ teinte cyan de la nappe, A ~0.27-1.0 — fade en
	// bord de rive). Multiplié à la teinte du matériau.
	ALBEDO = c * tint.rgb * COLOR.rgb;
	ALPHA = tint.a * COLOR.a;
	ROUGHNESS = 0.15;
	METALLIC = 0.0;
}
