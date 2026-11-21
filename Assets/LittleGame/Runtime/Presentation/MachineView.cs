using UnityEngine;
using System.Collections.Generic;

namespace LittleGame
{
    public sealed class MachineView : MonoBehaviour
    {
        public Transform Machine => model.transform;
        private MachineModel model;
        private WorkbenchView workbench;
        private CollectionBar collection;
        private GameObject deviceRoot;
        private int builtLevel;
        private float failureTime=-10;
        private int failureSequence;
        private string failurePart="";
        private Camera viewCamera;
        private float fitRadius;
        private readonly Dictionary<string, PartMotion> motions = new Dictionary<string, PartMotion>();
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        private void Start()
        {
            viewCamera=Camera.main;
            viewCamera.transform.SetPositionAndRotation(new Vector3(0,10,-6),Quaternion.Euler(PuzzleSession.CameraPitch,0,0));
            viewCamera.orthographic=true;viewCamera.orthographicSize=5.7f;
            viewCamera.backgroundColor=new Color(.105f,.125f,.15f);viewCamera.clearFlags=CameraClearFlags.SolidColor;
            var desk=new GameObject("Workbench");desk.transform.SetParent(transform,false);
            workbench=desk.AddComponent<WorkbenchView>();workbench.Build();
            BuildDevice();
            ConfigureLighting();
            gameObject.AddComponent<RepairHud>().Build();
        }

        private void BuildDevice()
        {
            if(deviceRoot!=null) { deviceRoot.SetActive(false);Destroy(deviceRoot); }
            motions.Clear();builtLevel=GameBootstrap.Session.LevelIndex;
            failureTime=-10;failureSequence=0;failurePart="";
            deviceRoot=new GameObject("DevicePresentation");deviceRoot.transform.SetParent(transform,false);
            var machine=new GameObject(builtLevel==1?"Handheld":"Computer");machine.transform.SetParent(deviceRoot.transform,false);
            model=machine.AddComponent<MachineModel>();model.Build(builtLevel);fitRadius=model.BoundingRadius;
            float aspect=Mathf.Min(viewCamera.aspect,9f/16);viewCamera.orthographicSize=Mathf.Max(5.7f,fitRadius/aspect+.35f);
            var bar=new GameObject("CollectionBar");bar.transform.SetParent(deviceRoot.transform,false);
            collection=bar.AddComponent<CollectionBar>();collection.Build(model,GameBootstrap.Session.Level,viewCamera);
            machine.transform.localScale=Vector3.one*(builtLevel==1?.78f:.69f);FitDeviceInView();
            foreach(var definition in GameBootstrap.Session.Level.Parts)
            {
                string id=definition.Id;
                motions.Add(id,new PartMotion(model.Parts[id],collection.Parking(id),collection.Scale(id),
                    collection.ParkingRotation,model.AssemblyBounds,fitRadius*machine.transform.lossyScale.x,viewCamera));
            }
        }

        public object InteractionSnapshot()
        {
            if(builtLevel!=GameBootstrap.Session.LevelIndex) BuildDevice();
            var positions=new List<object>();
            foreach(var item in motions)
            {
                var part=model.Parts[item.Key];
                var point=viewCamera.WorldToViewportPoint(part.position);
                Color tint=model.Materials[item.Key].GetColor(EmissionColor);
                var visual=viewCamera.WorldToViewportPoint(item.Value.VisualCenter);
                positions.Add(new { id=item.Key,x=point.x,y=point.y,
                    localPosition=new { x=part.localPosition.x,y=part.localPosition.y,z=part.localPosition.z },
                    worldPosition=new { x=part.position.x,y=part.position.y,z=part.position.z },
                    worldScale=new { x=part.lossyScale.x,y=part.lossyScale.y,z=part.lossyScale.z },
                    visualCenter=new {x=visual.x,y=visual.y},motion=new {stage=item.Value.Stage,frontClearance=item.Value.FrontClearance,outwardClearance=item.Value.OutwardClearance},
                    emission=new { r=tint.r,g=tint.g,b=tint.b },
                    detached=GameBootstrap.Session.Collected(item.Key),parked=GameBootstrap.Session.Recovered(item.Key),
                    accessible=GameBootstrap.Session.Accessible(item.Key) });
            }
            var center=viewCamera.WorldToViewportPoint(Machine.position);
            float radius=fitRadius*Machine.lossyScale.x/(2*viewCamera.orthographicSize);
            float trayBottom=collection.Bottom;
            float trayTop=collection.Top;
            return new { frame=Time.frameCount,width=Screen.width,height=Screen.height,
                safeArea=new { x=Screen.safeArea.x,y=Screen.safeArea.y,width=Screen.safeArea.width,height=Screen.safeArea.height },
                workspaceBounds=new { machineBottom=center.y-radius,machineTop=center.y+radius,trayBottom,trayTop,
                    clearance=trayBottom-center.y-radius },
                center=new { x=center.x,y=center.y },
                gesture=GameBootstrap.Input.Gesture, interactionVersion="double-click-disassemble",
                doubleClickSeconds=PuzzleInput.DoubleClickSeconds,collection=collection.Snapshot(),hud=RepairHud.Current.Snapshot(),
                feedback=new { failurePart, failureActive=Time.unscaledTime-failureTime<.65f, selected=GameBootstrap.Session.Selected,
                    selectionPulse=(Mathf.Sin(Time.unscaledTime*5)+1)*.5f },
                orbitSensitivity=PuzzleSession.OrbitSensitivity,parts=positions };
        }

        private void LateUpdate()
        {
            if(builtLevel!=GameBootstrap.Session.LevelIndex) BuildDevice();
            if(model==null) return;
            var state=GameBootstrap.Session;
            var orientation = state.Orientation;
            model.transform.rotation = new Quaternion(orientation.X, orientation.Y, orientation.Z, orientation.W);
            FitDeviceInView();
            if(failureSequence!=state.FailureSequence)
            {
                failureSequence=state.FailureSequence;
                failureTime=failureSequence==0?-10:Time.unscaledTime;failurePart=state.FailedPart;
            }
            foreach(var item in motions)
            {
                bool removed=state.Collected(item.Key);
                item.Value.Apply(removed,state);
                var material=model.Materials[item.Key];
                Color emission=Color.black;
                if(!removed && state.Selected==item.Key)
                    emission=new Color(.12f,.48f,.3f)*(.3f+.7f*(Mathf.Sin(Time.unscaledTime*5)+1)*.5f);
                float age=Time.unscaledTime-failureTime;
                if(!removed && age<.65f)
                {
                    if(item.Key==state.Blocker) emission=new Color(.55f,.25f,.015f);
                    if(item.Key==failurePart)
                    {
                    emission=new Color(1,.015f,.005f)*(.3f+.7f*Mathf.Abs(Mathf.Sin(age*23)));
                        // PartMotion每帧复原失败前的位置，抖动不会累计或污染规则。
                        item.Value.RestoreAttachedPose();
                        model.Parts[item.Key].position+=viewCamera.transform.right*(Mathf.Sin(age*66)*.085f*(1-age/.65f));
                    }
                }
                else if(!removed) item.Value.RestoreAttachedPose();
                material.SetColor(EmissionColor,emission);
            }
            model.Parts["ribbon"].gameObject.SetActive(state.Cable);
            collection.Refresh(state);
        }

        private void ConfigureLighting()
        {
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.55f,.64f,.73f);
            RenderSettings.ambientEquatorColor=new Color(.32f,.36f,.4f);
            RenderSettings.ambientGroundColor=new Color(.16f,.18f,.22f);
            foreach(var light in FindObjectsOfType<Light>())
            {
                if(light.type!=LightType.Directional) continue;
                light.transform.rotation=Quaternion.Euler(73,-25,0);
                light.intensity=1.15f;
                light.color=new Color(1,.95f,.87f);
                light.shadows=LightShadows.Soft;
                light.shadowStrength=.55f;
                light.shadowBias=.035f;
                light.shadowNormalBias=.18f;
            }
        }

        private void FitDeviceInView()
        {
            float aspect = Mathf.Min(viewCamera.aspect, 9f / 16);
            viewCamera.orthographicSize = Mathf.Max(5.7f, fitRadius / aspect + .35f);
            Machine.position=viewCamera.transform.up*(-viewCamera.orthographicSize*(builtLevel==1?.14f:.22f));
        }

    }
}
