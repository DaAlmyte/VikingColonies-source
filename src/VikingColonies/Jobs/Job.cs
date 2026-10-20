using VikingColonies.AI;
using VikingColonies.Citizens;

namespace VikingColonies.Jobs
{
    /// <summary>Base for every job's brain. Runs only on the computer that owns the settler.</summary>
    public abstract class Job
    {
        protected Job(Citizen citizen, CitizenAI ai)
        {
            Citizen = citizen;
            Ai = ai;
        }

        protected Citizen Citizen { get; }
        protected CitizenAI Ai { get; }

        public abstract void Tick(float dt);

        /// <summary>Called when the settler comes back after a pause (e.g. fleeing), so stale progress checks start over.</summary>
        public virtual void Resume()
        {
        }

        /// <summary>At dusk, before he goes indoors: true while he still finishes something first (the lumberjack delivers his load).</summary>
        public virtual bool FinishingBeforeNight(float dt) => false;

        /// <summary>Night has come and he goes indoors: he puts things away (the builder leaves his site till morning).</summary>
        public virtual void OnNight()
        {
        }

        /// <summary>Called when the settler stops running this brain (fired, job changed, another computer took over).</summary>
        public virtual void Stop()
        {
        }
    }
}
