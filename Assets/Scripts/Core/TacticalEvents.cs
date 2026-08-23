using System;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.Core
{
    // Bus de eventos estático que conecta los sistemas de juego entre sí sin acoplarlos directamente.
    public static class TacticalEvents
    {
        public static Action OnRouteDrawStarted;
        public static Action OnRouteDrawEnded;
        public static Action OnBallOutOfBounds;

        // Se dispara al marcar gol; lleva el id del equipo que marca (0 = Azul, 1 = Rojo).
        public static Action<int> OnGoalScored;

        // Se dispara al reiniciar el juego desde el centro del campo.
        public static Action OnMatchReset;

        // Se dispara una vez cuando termina el partido.
        public static Action OnMatchOver;

        // Se dispara al terminar la primera parte.
        public static Action OnHalftime;

        // Se dispara cuando un defensor alcanza al jugador con el balón (atacante, defensor).
        public static Action<TeamMember, TeamMember> OnClashInitiated;

        // Se dispara cuando el jugador con el balón dispara a puerta (tirador, portero).
        public static Action<TeamMember, TeamMember> OnShotInitiated;

        // Se dispara cuando la barra de tensión de un equipo se llena y entra en racha.
        public static Action<TeamId> OnTensionIgnited;

        // Se dispara cuando un duelo termina en falta, con el jugador que la cometió.
        public static Action<TeamMember> OnFoulCommitted;
    }
}
