using System;
using System.Collections.Generic;

namespace LittleGame
{
    public sealed class PartDefinition
    {
        public readonly string Id,Name;
        public readonly string[] Requires;
        public readonly bool Interior;
        public PartDefinition(string id,string name,bool interior,params string[] requires)
        { Id=id;Name=name;Interior=interior;Requires=requires; }
    }
    public sealed class DeviceLevel
    {
        public readonly string Title;
        public readonly PartDefinition[] Parts;
        public DeviceLevel(string title,params PartDefinition[] parts) { Title=title;Parts=parts; }
        public PartDefinition Find(string id) { return Array.Find(Parts,p=>p.Id==id); }
    }
    // 数据只描述拆卸依赖，不引用模型、平台或MCP。
    public static class DeviceLevels
    {
        public static readonly DeviceLevel[] All={
            new DeviceLevel("旧掌机",
                new PartDefinition("screw_a","左侧外壳螺丝",false),new PartDefinition("screw_b","右侧外壳螺丝",false),
                new PartDefinition("cover","后盖",false,"screw_a","screw_b"),new PartDefinition("cable","电池排线接头",true),
                new PartDefinition("battery","电池",true,"cable"),new PartDefinition("board_screw","主板固定螺丝",true),
                new PartDefinition("board","主板",true,"cable","battery","board_screw"),
                new PartDefinition("chassis","掌机机壳",false,"screw_a","screw_b","cover","cable","battery","board_screw","board")),
            new DeviceLevel("电脑机箱",
                new PartDefinition("screw_a","左上机箱螺丝",false),new PartDefinition("screw_b","右上机箱螺丝",false),
                new PartDefinition("case_screw_c","下侧机箱螺丝",false),new PartDefinition("cover","机箱侧板",false,"screw_a","screw_b","case_screw_c"),
                new PartDefinition("cable","主电源接头",true),new PartDefinition("power_unit","电源模块",true,"cable"),
                new PartDefinition("gpu_screw","显卡固定螺丝",true),new PartDefinition("gpu","独立显卡",true,"cable","gpu_screw"),
                new PartDefinition("ram_clip","内存卡扣",true),new PartDefinition("ram","内存条",true,"ram_clip"),
                new PartDefinition("cooler_screw","散热器固定螺丝",true),new PartDefinition("cooler","处理器散热器",true,"cable","cooler_screw"),
                new PartDefinition("board_screw","主板固定螺丝",true),new PartDefinition("board","电脑主板",true,"power_unit","gpu","ram","cooler","board_screw"),
                new PartDefinition("chassis","机箱框架",false,"screw_a","screw_b","case_screw_c","cover","cable","power_unit","gpu_screw","gpu","ram_clip","ram","cooler_screw","cooler","board_screw","board"))
        };
    }
}
