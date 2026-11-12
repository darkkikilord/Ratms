namespace Ratms.Networking
{
    /// <summary>
    ///  Tipos de daño en el juego. Cada uno tiene su propio color/símbolo
    ///  en el popup flotante para que el jugador entienda de dónde viene
    ///  el golpe sin tener que mirar el log.
    ///
    ///  Es un byte (no int) porque se manda por la red en cada hit; cuanto
    ///  más pequeño el paquete, mejor.
    /// </summary>
    public enum DamageType : byte
    {
        Generic   = 0,  // genérico, sin tipo concreto
        Explosion = 1,  // misil, granada, dinamita
        Fire      = 2,  // futuro (lanzallamas, napalm)
        Fall      = 3,  // caída (aún no implementado, pero reservado)
        Water     = 4,  // muerte por agua
        Melee     = 5   // futuro (cuerpo a cuerpo, bate)
    }
}
