/// <summary>单位片段的公共对象配置，解析及表现行为由组合组件负责。</summary>
public class TimelineClipUnitConfigBase : TimelineClipConfigBase
{
    /// <summary>作用单位，缺省保持旧配置的施法者语义。</summary>
    public TimelineUnitSubjectType Subject;
    /// <summary>作用单位所引用的绑定 Clip；空值使用片段开始时间前最近的同类绑定。</summary>
    public string SubjectBindingKey;
}
