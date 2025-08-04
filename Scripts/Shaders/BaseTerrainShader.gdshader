shader_type spatial;
//render_mode ambient_light_disabled;

uniform sampler2DArray colorTexture;
uniform sampler2DArray normalTexture;
uniform sampler2DArray roughnessTexture;
uniform sampler2DArray metallicTexture;
uniform int biomeAmount;

varying vec3 world_pos;
varying vec3 world_normal;
varying flat float textureDepth;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	textureDepth = COLOR.r * float(biomeAmount);
}

vec3 triplanar(sampler2DArray tex, vec3 blendWeights)
{
	vec3 scaled_pos = world_pos / 4.0;

    vec3 x_uvw = vec3(scaled_pos.zy, textureDepth);
	vec3 y_uvw = vec3(scaled_pos.xz, textureDepth);
    vec3 z_uvw = vec3(scaled_pos.xy, textureDepth);

	vec3 projectionX = texture(tex, x_uvw).rgb;
	vec3 projectionY = texture(tex, y_uvw).rgb;
    vec3 projectionZ = texture(tex, z_uvw).rgb;

	return projectionX * blendWeights.x + projectionY * blendWeights.y + projectionZ * blendWeights.z;
}

void fragment() {
    vec3 blendWeights = abs(world_normal);
    blendWeights /= (blendWeights.x + blendWeights.y + blendWeights.z);
	ALBEDO = triplanar(colorTexture, blendWeights);
	NORMAL = triplanar(normalTexture, blendWeights);
	ROUGHNESS = triplanar(roughnessTexture, blendWeights).x;
	METALLIC = triplanar(metallicTexture, blendWeights).x;
}

//void light() {
//	DIFFUSE_LIGHT += ATTENUATION;
//}
