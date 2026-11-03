shader_type spatial;
render_mode cull_back, specular_schlick_ggx;

// Splatmap terrain .fsm — 5 couches :
//   layer0 = texture de base (slot0)
//   layer1..4 = slots 1..4, posées l'une sur l'autre (lerp) avec les
//   canaux R,G,B,A de la splatmap 128×128 de la tuile (voir fragment).
// Canal→slot : R,G,B,A = slots 1..4 (meilleur raccord inter-tuiles,
// cf. fragment) ; la base n'a pas de canal.
//
// UV vertex = position monde / 64 (1.0 par tuile) ; fract() donne
// l'UV locale 0..1 de la splatmap ; chaque couche est échantillonnée à
// wuv * uv_scaleN (uvScale du TexSlot natif).
//
// COLOR (attr vertex #3 du fsm) = éclairage baké par vertex — les
// valeurs réelles vont de ~0.4 à 1.0 (ombres portées sous les objets,
// teinte froide dans le bleu : B≥G≥R typique). Vérifié non-blanc sur
// 40+ maps (ex. S009 min 0.0, S051 ~0.85) → multiplié à l'albedo.

// PAS de source_color : la splatmap transporte des POIDS (données),
// pas une couleur — le hint décodait les octets en sRGB→linéaire
// (poids 0.62 → 0.34), ce qui écrasait les couches blend et laissait
// la texture de base dominer → sol « plat gris » (S002). Les poids
// natifs sont bruts (D3D9, upload sans conversion — rf_game
// FUN_00afe400 @0x00afe400 : swap R↔B octet à octet, pas de sRGB).
uniform sampler2D splat_map : filter_linear;
uniform sampler2D layer0 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D layer1 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D layer2 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D layer3 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D layer4 : source_color, filter_linear_mipmap, repeat_enable;
// Lightmap native de la tuile (blanc si absente) : sans mipmaps,
// clamp — D3D9 la pose sur l'UV set 0.
uniform sampler2D dark_map : source_color, filter_linear, repeat_disable;
uniform float uv_scale0 = 1.0;
uniform float uv_scale1 = 1.0;
uniform float uv_scale2 = 1.0;
uniform float uv_scale3 = 1.0;
uniform float uv_scale4 = 1.0;
// Diagnostic : ≥0 → n'affiche que la couche N (ou la splatmap si N≥5).
uniform float debug_layer = -1.0;

varying vec2 wuv;
// UV locale 0..1 de la tuile (VERTEX local / 64) : fract(wuv) tombait sur
// 0.0 au lieu de 1.0 pour les fragments de la rangée de bord (UV=k+eps)
// -> ligne de texel opposé = couture aux frontières de tuile (A6.4).
varying vec2 tile_uv;

void vertex() {
	wuv = UV;
	tile_uv = VERTEX.xy / 64.0;
}

void fragment() {
	// Texels 0 et 127 de la splat = valeur PARTAGÉE avec la tuile voisine
	// (moyenne dans le même canal) : fausse quand les deux tuiles n'ont pas
	// la même texture dans ce canal (palettes par tuile). Le poids d'une
	// même TEXTURE est continu entre l'intérieur (texels 126 / 1) des deux
	// tuiles (écart ~10/255 contre ~125 par canal, S001/S003) : on ne
	// lit donc jamais les texels de bord.
	vec4 s = texture(splat_map, clamp(tile_uv * 128.0, vec2(1.5), vec2(126.5)) / 128.0);
	// Mélange natif = EMPILEMENT de « decals » (Gamebryo : chaque slot
	// 1..4 est posé par-dessus le résultat précédent avec l'alpha de
	// son canal R,G,B,A de la splatmap) : c = lerp(c, texN, canalN),
	// base slot0 d'abord. Les canaux ne sont PAS des poids additifs :
	// leur somme dépasse souvent 1 (S001/S002) et l'ancienne
	// normalisation (somme des poids) faisait diverger deux tuiles
	// voisines aux frontières → grandes plaques de teinte/texture
	// différentes, coupées net au bord de tuile. Preuve chiffrée :
	// écart splat→texture entre 2 tuiles voisines, S001 : poids
	// additifs 0.119 (p90 0.51) contre empilement 0.057 (p90 0.09).
	vec3 c = texture(layer0, wuv * uv_scale0).rgb;
	c = mix(c, texture(layer1, wuv * uv_scale1).rgb, s.r);
	c = mix(c, texture(layer2, wuv * uv_scale2).rgb, s.g);
	c = mix(c, texture(layer3, wuv * uv_scale3).rgb, s.b);
	c = mix(c, texture(layer4, wuv * uv_scale4).rgb, s.a);
	// DarkMap natif (lightmap de tuile, « <n>DarkMap.dds », nom stocké
	// dans le record @+0x1aa58) : UV set 0 = 0..1 sur la tuile (comme
	// l'alpha), multiplicatif sur la base.
	vec3 dark = texture(dark_map, clamp(tile_uv, 0.0, 1.0)).rgb;
	if (debug_layer > 9.5) {
		c = dark;
	} else if (debug_layer > 8.5) {
		c = COLOR.rgb;
	} else if (debug_layer > 7.5) {
		c = vec3(1.0 - max(max(s.r, s.g), max(s.b, s.a)));
	} else if (debug_layer > 6.5) {
		c = vec3(clamp(tile_uv, 0.0, 1.0), 0.0);
	} else if (debug_layer > 5.5) {
		c = texture(layer0, clamp(tile_uv, 0.0, 1.0)).rgb;
	} else if (debug_layer > 4.5) {
		c = s.rgb;
	} else if (debug_layer > -0.5) {
		c = debug_layer < 0.5 ? texture(layer0, wuv * uv_scale0).rgb
		    : debug_layer < 1.5 ? texture(layer1, wuv * uv_scale1).rgb
		    : debug_layer < 2.5 ? texture(layer2, wuv * uv_scale2).rgb
		    : debug_layer < 3.5 ? texture(layer3, wuv * uv_scale3).rgb
		    : texture(layer4, wuv * uv_scale4).rgb;
	}
	// Diagnostic-only isolated layer : sortie non éclairée — un
	// éclairage/vertex-color corrompu ne peut pas passer pour une
	// texture manquante.
	if (debug_layer >= 0.0) {
		ALBEDO = vec3(0.0);
		EMISSION = c;
	} else {
		ALBEDO = c * COLOR.rgb * dark;
	}
	ROUGHNESS = 1.0;
}
