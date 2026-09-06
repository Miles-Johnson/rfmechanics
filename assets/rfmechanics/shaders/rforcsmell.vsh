#version 330 core
layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 uvIn;
layout(location = 2) in vec4 rgbaIn;
uniform mat4 projectionMatrix;
out vec2 uv;
out vec4 color;
void main() {
    uv = uvIn;
    color = rgbaIn;
    gl_Position = projectionMatrix * vec4(vertexPosition, 1.0);
}
