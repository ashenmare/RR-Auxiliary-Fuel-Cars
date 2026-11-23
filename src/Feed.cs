using Helpers;
using KeyValue.Runtime;
using Model;

namespace AuxFuelCars
{
    // The per-car Feed checkbox. Stored in the car's own KeyValue object, the same way the MoW work car keeps its
    // Enabled box (a dictionary under one key, written whole): saved with the car and synced to every player by the
    // game. Writing it needs crew access to the car, as the MoW box does (Car.AuthorizationRequirementForPropertyWrite).
    // Missing (every car until someone unticks it) means on.
    internal static class Feed
    {
        internal static bool IsOn(Car car)
        {
            Value v = car.KeyValueObject[Ids.KvKey][Ids.FeedKey];
            return v.IsNull || v.BoolValue;
        }

        internal static void Set(Car car, bool on)
        {
            new KeyValueProperties(car.KeyValueObject, Ids.KvKey)[Ids.FeedKey] = Value.Bool(on);
        }

        internal static bool CanWrite(Car car) => new KeyValueProperties(car.KeyValueObject, Ids.KvKey).AuthorizedToWrite;
    }
}
