using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Flynn.Rendering
{
    /// <summary>
    /// Fullscreen pass that darkens and desaturates everything outside the lit zones.
    ///
    /// Injected AFTER post-processing deliberately: this scene's brightness is dominated by the
    /// post stack (exposure lift + bloom), not by the Global Light2D, so a "darkness" expressed
    /// anywhere earlier in the frame gets lifted straight back out. Here it is the last word.
    ///
    /// Knows nothing about lanterns — it only reads shader globals, which keeps this stable
    /// rendering infra with no dependency on any gameplay module. LitZoneMaskDriver pushes them.
    /// </summary>
    public class LitZoneMaskFeature : ScriptableRendererFeature
    {
        [Tooltip("Material using Flynn/LitZoneMask. Left empty the feature does nothing.")]
        public Material material;

        [Tooltip("Editor-only: run in the Scene view too. Off keeps scene authoring at full light.")]
        public bool affectSceneView;

        private LitZonePass _pass;

        public override void Create()
        {
            // BEFORE post, not after: on the 2D renderer the post pass consumes the camera color
            // target and writes to the backbuffer, so at AfterRenderingPostProcessing the handle
            // this pass samples is dead — the world blits black (seen 2026-07-30). Post's exposure
            // lift scales lit and dark alike; the contrast survives tonemapping fine.
            _pass = new LitZonePass
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            if (material == null) return;

            var cam = data.cameraData.cameraType;
            if (cam == CameraType.Preview || cam == CameraType.Reflection) return;
            if (cam == CameraType.SceneView && !affectSceneView) return;

            _pass.Setup(material);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing) => _pass?.Dispose();

        // ------------------------------------------------------------------

        private class LitZonePass : ScriptableRenderPass
        {
            private Material _material;
            private RTHandle _temp;
            private static readonly int CamRect = Shader.PropertyToID("_LitZoneCamRect");

            public void Setup(Material m) => _material = m;

            public void Dispose() => _temp?.Release();

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData data)
            {
                var desc = data.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;
                RenderingUtils.ReAllocateIfNeeded(ref _temp, desc, name: "_LitZoneTemp");
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData data)
            {
                if (_material == null || _temp == null) return;

                var camData = data.cameraData;
                var cam = camData.camera;

                // Orthographic screen->world is a plain rescale; the shader needs no matrices.
                float halfH = cam.orthographicSize;
                float halfW = halfH * cam.aspect;
                Vector3 p = cam.transform.position;
                _material.SetVector(CamRect, new Vector4(p.x, p.y, halfW, halfH));

                var cmd = CommandBufferPool.Get("LitZoneMask");
                var source = camData.renderer.cameraColorTargetHandle;

                // Blit cannot read and write the same target, so bounce via a temp and back.
                Blitter.BlitCameraTexture(cmd, source, _temp, _material, 0);
                Blitter.BlitCameraTexture(cmd, _temp, source);

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
