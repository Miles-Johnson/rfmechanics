#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 uvIn;

uniform mat4 projectionMatrix;
uniform mat4 modelViewMatrix;

out vec2 uv;

void main () {
	uv = uvIn;
	gl_Position = projectionMatrix * modelViewMatrix * vec4(vertexPosition, 1.0);
}
