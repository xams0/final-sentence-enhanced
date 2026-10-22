-- Lanppart3 settings. Edit and restart the game to apply.
--
-- With the mod, every lobby game is hosted by the lobby host (the other members, who also run the mod,
-- join it automatically). In the lobby window, unchecking "Jouer à la roulette russe" shows the "Vies"
-- line, in every mode; "Lanppart Poules" and "Lanppart Finales" are presets in the mode list.
return {
    -- Lives preselected when the roulette gets unchecked, and used by the Ctrl+F8 shortcut (1 to 6).
    FinalesLives = 3,

    -- How members reach the host:
    --   nil              -> Steam P2P through the host's Steam account (no port forwarding needed)
    --   "192.168.1.20"   -> plain IP (LAN, or the host forwards UDP port 7777)
    HostAddress = nil,

    -- Test shortcuts without a lobby: Ctrl+F8 hosts a duel with lives, Ctrl+F9 joins TestJoinAddress.
    TestJoinAddress = "127.0.0.1",
}
