using System;
using UnityEngine;
using Farm.Farming;
using Farm.Net;

namespace Farm.Game
{
    /// <summary>
    /// Переезды между фермами поверх <see cref="GameFlow"/>: в гости и домой.
    /// <para>
    /// Как и вход из меню, вся сетевая асинхронщина заканчивается до смены сцены:
    /// снимок добывается здесь, сцена получает готовый <see cref="PendingLoad"/>.
    /// Любой сбой — вслух через <see cref="NetStatus"/> и без переезда: остаться на
    /// своей ферме — всегда безопасный исход.
    /// </para>
    /// </summary>
    public static class OnlineFlow
    {
        /// <summary>Переезд уже готовится — второй клик по «В гости» не должен начать второй.</summary>
        private static bool _busy;

        /// <summary>
        /// Поехать в гости к другу. Свой прогресс перед отъездом уезжает на сервер:
        /// вернёмся мы тоже с сервера, и несохранённые минуты иначе бы пропали.
        /// </summary>
        public static async void Visit(int ownerId, string ownerName)
        {
            if (_busy || GuestMode.IsGuest || !NetSession.LoggedIn) return;
            _busy = true;

            try
            {
                NetStatus.Set("еду в гости: " + ownerName + "…");
                SaveRunner.SaveIfPossible("перед визитом");

                var farm = await ApiClient.GetFarmAsync(ownerId);
                if (!farm.Transport) return;   // NetStatus уже рассказал
                if (farm.Value == null || !farm.Value.ok)
                {
                    NetStatus.Fail("визит", farm.Value != null ? farm.Value.error : "непонятный ответ");
                    return;
                }
                if (!farm.Value.found)
                {
                    NetStatus.Set("у " + ownerName + " ещё нет фермы");
                    return;
                }

                FarmSaveData data = null;
                try { data = JsonUtility.FromJson<FarmSaveData>(farm.Value.state); }
                catch (Exception e) { Debug.LogError("[Визит] Снимок не разобрался: " + e.Message); }
                if (data == null)
                {
                    NetStatus.Fail("визит", "снимок фермы не читается");
                    return;
                }

                // Дельту считаем и гостю: он должен видеть ферму «как сейчас», с доросшими
                // грядками, а не как в момент последнего сейва хозяина.
                double offline = Math.Max(0.0, farm.Value.serverNow - farm.Value.savedAt);

                GameFlow.StartFarm(new PendingLoad
                {
                    Data = data,
                    OfflineSeconds = offline,
                    Source = "визит",
                    IsGuest = true,
                    OwnerId = ownerId,
                    OwnerName = ownerName,
                });
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// Вернуться из гостей домой. Своя ферма берётся с сервера; если он замолчал,
        /// пока мы гостили, — с локального сейва, который визит не трогал.
        /// </summary>
        public static async void GoHome()
        {
            if (_busy) return;
            _busy = true;

            try
            {
                NetStatus.Set("возвращаюсь домой…");

                var farm = await ApiClient.GetFarmAsync();
                if (!farm.Transport || farm.Value == null || !farm.Value.ok || !farm.Value.found)
                {
                    NetStatus.Set("сервер молчит — домой по локальному сейву");
                    GameFlow.ContinueLocal();
                    return;
                }

                FarmSaveData data = null;
                try { data = JsonUtility.FromJson<FarmSaveData>(farm.Value.state); }
                catch (Exception e) { Debug.LogError("[Домой] Снимок не разобрался: " + e.Message); }
                if (data == null)
                {
                    NetStatus.Set("серверный снимок не читается — домой по локальному сейву");
                    GameFlow.ContinueLocal();
                    return;
                }

                NetSession.FarmRev = farm.Value.rev;
                double offline = Math.Max(0.0, farm.Value.serverNow - farm.Value.savedAt);
                GameFlow.StartFarm(new PendingLoad { Data = data, OfflineSeconds = offline, Source = "сервер" });
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
