-- lapis.lua — dissector Wireshark du protocole Eden (lapis).
--
-- Trame TCP : [varint len][u16 ncid][corps] — len = taille du
-- payload NCID inclus, préfixe LEB128 (7 bits/octet, bit7 =
-- continuation, 1 à 5 octets ; remplace le u16 historique depuis le
-- cutover phase-4b). Le corps est chiffré RC4 après le handshake
-- RSA — le dissector n'essaie PAS de déchiffrer (clé par session,
-- dérivation RSA côté serveur) ; il cadre et expose le NCID. Utile
-- surtout avec ZONE_CRYPTO=0 ou pour observer le handshake.
--
-- Installation : copier dans ~/.local/lib/wireshark/plugins/ puis
-- redémarrer Wireshark. Ports couverts : 5200/5201 (world),
-- 10022 (zone), 2067 (ticket), 5301 (world-link), 7654 (mission).
--
-- Source de vérité : server/src/Eden.Core/Networking/
-- (VarintCodec, PacketReceiveBuffer).

local lapis = Proto("lapis", "Eden lapis BinStream")

local f_len  = ProtoField.uint32("lapis.len",  "Body length", base.DEC)
local f_ncid = ProtoField.uint16("lapis.ncid", "NCID",        base.DEC)
local f_body = ProtoField.bytes ("lapis.body", "Body (RC4)")
lapis.fields = { f_len, f_ncid, f_body }

-- Décode le préfixe varint à `offset`.
-- Retourne (len, taille du préfixe) ou nil si incomplet (le
-- dissector demandera alors plus de données).
local function decode_varint(tvb, offset)
    local value = 0
    local shift = 0
    for i = 0, 4 do
        if offset + i >= tvb:len() then
            return nil                     -- préfixe incomplet
        end
        local b = tvb(offset + i, 1):uint()
        value = value + bit.band(b, 0x7F) * (2 ^ shift)
        if bit.band(b, 0x80) == 0 then
            return value, i + 1
        end
        shift = shift + 7
    end
    return nil                             -- overlong : traité comme
                                           -- incomplet (jamais concluant)
end

-- Longueur totale de la PDU : taille préfixe varint + len.
-- 0 = besoin de plus de données (préfixe incomplet).
local function get_pdu_len(tvb, pinfo, offset)
    local len, hdr = decode_varint(tvb, offset)
    if len == nil then return 0 end
    return hdr + len
end

local function dissect_frame(tvb, pinfo, tree)
    pinfo.cols.protocol = "lapis"
    local subtree = tree:add(lapis, tvb(), "Eden lapis BinStream")
    local len, hdr = decode_varint(tvb, 0)
    if len == nil then return 0 end
    subtree:add_le(f_len, tvb(0, hdr))
    if tvb:len() > hdr then
        subtree:add_le(f_ncid, tvb(hdr, 2))
        local ncid = tvb(hdr, 2):le_uint()
        pinfo.cols.info = "NCID=" .. ncid
    end
    if tvb:len() > hdr + 2 then
        subtree:add(f_body, tvb(hdr + 2))
    end
    return tvb:len()
end

function lapis.dissector(tvb, pinfo, tree)
    -- Réassemblage TCP : get_pdu_len rend 0 tant que le préfixe
    -- varint est incomplet → Wireshark attend la suite.
    dissect_tcp_pdus(tvb, tree, 1, get_pdu_len, dissect_frame)
end

local default_ports = { 5200, 5201, 10022, 2067, 5301, 7654 }
for _, port in ipairs(default_ports) do
    DissectorTable.get("tcp.port"):add(port, lapis)
end
