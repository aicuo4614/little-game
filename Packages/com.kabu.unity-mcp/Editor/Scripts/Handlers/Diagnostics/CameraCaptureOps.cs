using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KabuUnityMcp.Editor.Handlers.Diagnostics
{
    internal static class CameraCaptureOps
    {
        public static HandlerResult Capture(int cameraInstanceId, int width, int height)
        {
            if (width < 16 || width > 2048 || height < 16 || height > 2048)
            {
                return HandlerResult.Fail("width and height must be 16..2048.", -32602);
            }
            var target = EditorUtility.InstanceIDToObject(cameraInstanceId);
            var camera = target as Camera ?? (target as GameObject)?.GetComponent<Camera>();
            if (!camera)
            {
                return HandlerResult.Fail("cameraInstanceId must identify a Camera or camera GameObject.", -32602);
            }
            var previousActive = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            RenderTexture render = null;
            Texture2D image = null;
            try
            {
                render = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = render;
                camera.Render();
                RenderTexture.active = render;
                image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/KabuUnityMcp/Captures"));
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                return HandlerResult.Success(new { schema = "kabu.camera.capture/v1", path, width, height,
                    cameraInstanceId = camera.GetInstanceID(), sampledFrame = Time.frameCount,
                    capturedUtc = DateTime.UtcNow.ToString("o"),
                    note = "Explicit camera render, not a GameView screenshot. Screen-space overlay UI and editor chrome are not included; render callbacks may execute." });
            }
            finally
            {
                if (camera)
                {
                    camera.targetTexture = previousTarget;
                }
                RenderTexture.active = previousActive;
                if (image)
                {
                    UnityEngine.Object.DestroyImmediate(image);
                }
                if (render)
                {
                    RenderTexture.ReleaseTemporary(render);
                }
            }
        }
    }
}
