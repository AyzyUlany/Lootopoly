# ⚔️ Lootopoly

**Lootopoly** is a fully on-chain competitive board game built on Avalanche. Monopoly meets JRPG combat with trustless escrows and Chainlink VRF.

### 🔗 Quick Links
* **Live MVP (Play Here):** https://lootopoly.vercel.app/
* **Smart Contract (Fuji Testnet):**[https://testnet.snowtrace.io/address/0x05d65E6c0fe516Dc296C80Eb448848DAD35222fC](https://testnet.snowtrace.io/address/0x05d65E6c0fe516Dc296C80Eb448848DAD35222fC)
* **Video Demo:** (https://youtu.be/xGZg4jkYgVU)

### 🎮 How It Works
1. **Connect Core/MetaMask:** Ensure you are on the Avalanche Fuji Testnet.
2. **Join a Room:** Pay a 10% bond to enter a lobby. 
3. **Commit Fee:** Once the lobby is full, a 5-minute commit window opens. Pay the rest of your entry fee to lock your spot.
4. **Deterministic VRF:** The smart contract calls Chainlink VRF 2.5. The resulting random seed is passed to the Unity WebGL client.
5. **Play:** Roll dice, buy property, fight monsters, and play action cards. All RNG is safely derived from the VRF seed.
6. **Winner Takes All:** The last player standing receives the pooled AVAX automatically via the smart contract.

### 🛠️ Tech Stack
* **Frontend/Game Engine:** Unity 6.3 (WebGL), HTML5/CSS3/Vanilla JS
* **Web3 Integration:** Ethers.js v6.7.0
* **Smart Contracts:** Solidity ^0.8.26
* **Oracle:** Chainlink VRF V2.5
