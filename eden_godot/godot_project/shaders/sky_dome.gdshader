shader_type spatial;
render_mode unshaded, cull_disabled, fog_disabled;

// Dôme de ciel natif (map/model/sky/<code>.glb) — la texture est la
// couleur finale (non éclairée), le brouillard ne doit PAS l'atteindre
// sinon le dôme à ~2 km serait 100 % fog. Le nœud suit la caméra —
// voir SkyDome.cs.
//
// ALPHA : le quad « sun » (alphaMode BLEND) et les textures de bord
// exploitent le canal alpha de la texture.

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_disable;
uniform vec4 modulate : source_color = vec4(1.0);

void fragment() {
	vec4 c = texture(albedo_tex, UV) * modulate;
	ALBEDO = c.rgb;
	ALPHA = c.a;
}
