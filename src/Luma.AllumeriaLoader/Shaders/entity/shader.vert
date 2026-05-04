#version 330 core
// Luma entity shader model light samples.
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec2 aTexCoord;
layout (location = 2) in uint normalByte;
layout (location = 5) in uint boneByte;

out vec2 texCoord;
out vec4 vertexCol;

out vec3 fragPosition; //for fog

uniform mat4 transform;
uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

uniform vec4 ambientColor;
uniform vec4 light;

uniform int boneCount;
uniform mat4 boneMatrices[20]; //max limit of 20 matrices

const int MAX_MODEL_LIGHT_SAMPLES = 16;
uniform int modelLightSampleCount;
uniform vec3 modelLightPositions[MAX_MODEL_LIGHT_SAMPLES];
uniform vec4 modelLightValues[MAX_MODEL_LIGHT_SAMPLES];

const vec3 normals[6] = vec3[6](
    vec3(0,1,0),
    vec3(0,-1,0),
    vec3(1,0,0),
    vec3(-1,0,0),
    vec3(0,0,1),
    vec3(0,0,-1)
);

const float directionalColors[6] = float[6](
    1,0.7,0.8,0.8,0.9,0.9
);

const float lightMultiplier = 0.0666;

vec3 unpackWorldLight(vec4 lightValue)
{
    return max(
        lightValue.xyz * lightMultiplier,
        ambientColor.xyz * lightValue.w * lightMultiplier);
}

vec3 sampleModelLight(vec3 worldPosition)
{
    if (modelLightSampleCount <= 0)
    {
        return unpackWorldLight(light);
    }

    vec3 totalLight = vec3(0,0,0);
    float totalWeight = 0.0;

    for (int i = 0; i < MAX_MODEL_LIGHT_SAMPLES; i++)
    {
        if (i >= modelLightSampleCount)
        {
            break;
        }

        float dist = distance(modelLightPositions[i], worldPosition);
        float weight = 1.0 / max(dist * dist, 0.18);
        totalLight += unpackWorldLight(modelLightValues[i]) * weight;
        totalWeight += weight;
    }

    if (totalWeight <= 0.0001)
    {
        return unpackWorldLight(light);
    }

    return clamp(totalLight / totalWeight, vec3(0,0,0), vec3(1,1,1));
}

void main()
{
    vec4 skinnedPosition = vec4(aPos, 1.0) * boneMatrices[boneByte];
    vec4 worldPosition = skinnedPosition * model;
    fragPosition = worldPosition.xyz;
    gl_Position = worldPosition * view * projection;
    texCoord = vec2(aTexCoord.x, aTexCoord.y);

    vertexCol = vec4(sampleModelLight(worldPosition.xyz) * directionalColors[normalByte], 1);
}
