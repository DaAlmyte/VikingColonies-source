using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VikingColonies.Buildings;
using VikingColonies.Config;
using VikingColonies.Core;

namespace VikingColonies.Citizens
{
    /// <summary>
    /// A settler's meal, on the computer running him: once a day, after dawn, he eats the plainest food in his colony's
    /// chests (the Town Hall's first), taken where he stands; with nothing to eat he's hungry till the next day (<see cref="Meals"/>).
    /// </summary>
    internal sealed class MealTime
    {
        private const float CheckSeconds = 3f;
        /// <summary>A chest another computer holds may keep him waiting; past this he goes hungry.</summary>
        private const float PatienceSeconds = 60f;

        private float _timer = CheckSeconds;
        private float _lookingSince = -1f;
        private readonly CarryLoad _plate = new CarryLoad();

        /// <param name="atTable">His breakfast routine serves today's meal (0.15): this only eats for him when it gave up.</param>
        public void Tick(Citizen citizen, float dt, bool atTable = false)
        {
            _timer -= dt;
            if (_timer > 0f) return;
            _timer = CheckSeconds;
            CitizenRecord r = citizen.Record;
            if (!ColonyConfig.SettlersEat.Value || !r.HasColony || EnvMan.instance == null || ZNet.instance == null) return;
            int today = EnvMan.instance.GetDay(ZNet.instance.GetTimeSeconds());
            if (Meals.FirstDay(r.LastMealDay))
            {
                citizen.MarkMeal(today, hungry: false);   // he came fed: the player gets a day to stock food
                return;
            }
            if (!Meals.Due(r.LastMealDay, today, EnvMan.IsNight()) || atTable)
            {
                _lookingSince = -1f;
                return;
            }
            if (_lookingSince < 0f) _lookingSince = Time.time;
            var stock = new Dictionary<string, int>();   // all the colony's food
            var spare = new Dictionary<string, int>();   // what buildings don't keep (0.21)
            var where = new Dictionary<string, List<StorageModule>>();
            foreach (ColonyBuilding store in ColonyBuilding.All
                         .Where(b => b != null && b.IsValid && b.ColonyId == r.ColonyId && b.Storage != null)
                         .OrderByDescending(b => b.IsTownHall).ThenBy(b => Vector3.Distance(b.transform.position, citizen.transform.position)))
            {
                foreach (KeyValuePair<string, int> item in store.Storage.Contents())
                {
                    if (FoodItems.Value(item.Key) <= 0f) continue;
                    stock[item.Key] = (stock.TryGetValue(item.Key, out int n) ? n : 0) + item.Value;
                    if (!where.TryGetValue(item.Key, out List<StorageModule> chests)) where[item.Key] = chests = new List<StorageModule>();
                    chests.Add(store.Storage);
                }
                foreach (KeyValuePair<string, int> item in store.Storage.SpareContents())
                    if (FoodItems.Value(item.Key) > 0f) spare[item.Key] = (spare.TryGetValue(item.Key, out int n) ? n : 0) + item.Value;
            }
            // Kept food is left alone, unless there is nothing else in the colony: then he eats it rather than go hungry.
            bool keptOnly = spare.Count == 0;
            string food = Meals.Choose(FoodItems.In(keptOnly ? stock : spare), r.LastMeals);   // not what he had lately, when there's a choice
            if (food != null && where[food].Any(chest => chest.TryTakeItem(food, _plate, ownUse: keptOnly)))   // a busy chest, or none spare there: the next one
            {
                _plate.Clear();
                citizen.MarkMeal(today, hungry: false, item: food);
                if (keptOnly) Log.Info($"{r.Name} ate kept food ({FoodItems.Name(food)}): nothing else in the colony (day {today}).");
                else Log.Info($"{r.Name} ate {FoodItems.Name(food)} (day {today}).");
                return;
            }
            if (food != null && Time.time - _lookingSince < PatienceSeconds) return;   // the chest is busy: he asks again
            citizen.MarkMeal(today, hungry: true);
            Log.Info($"{r.Name} found nothing to eat (day {today}); he's hungry.");
        }
    }
}
