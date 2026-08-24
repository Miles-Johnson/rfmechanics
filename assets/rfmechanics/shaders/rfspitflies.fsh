#version 330 core

in vec2 uv;
out vec4 outColor;

uniform sampler2D tex2d;
uniform vec4 rgbaTint;
uniform float opacity;

void main() {
	vec4 texColor = texture(tex2d, uv);
	float alpha = texColor.a * opacity;
	if (alpha < 0.01) discard;
	outColor = vec4(texColor.rgb * rgbaTint.rgb, alpha * rgbaTint.a);
}
