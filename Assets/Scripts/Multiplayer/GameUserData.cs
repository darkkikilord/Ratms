using System;
using Unity.Collections;
using Unity.Netcode;

namespace Scripts.Core.NET
{
    // This struct defines the data for a single player in the lobby/game.
    // We use a struct because it's a small, simple data container.
    // - INetworkSerializable: Allows this struct to be sent over the network.
    // - IEquatable<GameUserData>: Allows the NetworkList to efficiently compare
    //   two instances to see if they are different, preventing unnecessary network traffic.
    public struct GameUserData : INetworkSerializable, IEquatable<GameUserData> 
    {
        public ulong playerId;
        public bool isReady;
    
        // For strings in a NetworkList, we must use a fixed-size string type.
        public FixedString64Bytes playerName;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref playerId);
            serializer.SerializeValue(ref isReady);
            serializer.SerializeValue(ref playerName);
        }

        public bool Equals(GameUserData other)
        {
            return playerId == other.playerId &&
                   isReady == other.isReady &&
                   playerName == other.playerName;
        }
    }
}