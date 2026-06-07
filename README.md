# Star Wars: Outer Rim — Digital Edition

**Unity 3D | Turn-Based Multiplayer Board Game**

Digital adaptation of the Star Wars: Outer Rim board game. 2–4 players compete as scoundrels, bounty hunters, and smugglers in the Star Wars underworld.

## Tech Stack

- **Engine:** Unity 2022.3 LTS
- **Networking:** Unity Netcode for GameObjects (NGO) + Unity Relay + Unity Lobby
- **Language:** C# (.NET Standard 2.1)
- **Auth Model:** Host-server via Unity Relay (no dedicated server required)

## Architecture

See [docs/TDD.md](docs/TDD.md) for the full Technical Design Document covering:
- Multiplayer architecture (NGO host-server model)
- State machine (Planning → Action → Encounter → Win Check)
- BFS-based node movement & pathfinding
- Market deck system with 6 card types
- Dice & combat resolution
- ScriptableObject data pipeline

## Setup

1. Install Unity 2022.3 LTS with WebGL and IL2CPP modules
2. Clone this repo
3. Open in Unity Hub → Add project from disk
4. Install NGO, Relay, and Lobby packages via Package Manager

## Status

🚧 Pre-production / Technical Design phase
