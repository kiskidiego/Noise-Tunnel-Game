shader_type spatial;
//render_mode ambient_light_disabled;

uniform sampler2DArray colorTexture;
uniform sampler2DArray normalTexture;
uniform sampler2DArray metalRoughTexture;
uniform int biomeAmount;

varying vec3 world_pos;
varying vec3 world_normal;
varying flat vec3 biomeIndexes;
varying vec3 biomeInfluences;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	biomeIndexes = COLOR.rgb * float(biomeAmount);
	biomeInfluences = vec3(COLOR.a, UV);
}

vec3 triplanar(sampler2DArray tex, vec3 blendWeights, vec3 scaled_pos, float textureDepth)
{
    vec3 x_uvw = vec3(scaled_pos.zy, textureDepth);
	vec3 y_uvw = vec3(scaled_pos.xz, textureDepth);
    vec3 z_uvw = vec3(scaled_pos.xy, textureDepth);

	vec3 projectionX = texture(tex, x_uvw).rgb;
	vec3 projectionY = texture(tex, y_uvw).rgb;
    vec3 projectionZ = texture(tex, z_uvw).rgb;

	return projectionX * blendWeights.x + projectionY * blendWeights.y + projectionZ * blendWeights.z;
}

void fragment() {
	vec3 scaled_pos = world_pos / 4.0;
    vec3 blendWeights = abs(world_normal);
    blendWeights = pow(blendWeights, vec3(4.0)); // Sharper transitions
    blendWeights /= dot(blendWeights, vec3(1.0));

	ALBEDO = triplanar(colorTexture, blendWeights, scaled_pos, biomeIndexes.r) * biomeInfluences.r +
			 triplanar(colorTexture, blendWeights, scaled_pos, biomeIndexes.g) * biomeInfluences.g +
			 triplanar(colorTexture, blendWeights, scaled_pos, biomeIndexes.b) * biomeInfluences.b;

	vec3 metalRough = triplanar(metalRoughTexture, blendWeights, scaled_pos, biomeIndexes.r) * biomeInfluences.r +
					  triplanar(metalRoughTexture, blendWeights, scaled_pos, biomeIndexes.g) * biomeInfluences.g +
					  triplanar(metalRoughTexture, blendWeights, scaled_pos, biomeIndexes.b) * biomeInfluences.b;

	METALLIC = metalRough.r;
	ROUGHNESS = metalRough.g;
/**
	vec3 normalMap = triplanar(normalTexture, blendWeights, scaled_pos, biomeIndexes.r) * biomeInfluences.r +
			 		 triplanar(normalTexture, blendWeights, scaled_pos, biomeIndexes.g) * biomeInfluences.g +
					 triplanar(normalTexture, blendWeights, scaled_pos, biomeIndexes.b) * biomeInfluences.b;

	NORMAL = normalize(normalMap);/**/
}

//void light() {
//	DIFFUSE_LIGHT += ATTENUATION;
//}
