#version 330 core
in vec2 uv;
in vec4 color;
out vec4 outColor;
void main() {
    float radius = length(uv * 2.0 - 1.0);
    float alpha = color.a * (1.0 - smoothstep(0.15, 1.0, radius));
    if (alpha < 0.002) discard;
    outColor = vec4(color.rgb, alpha);
}
