-- pdn-lin: keep the desktop's sound server off the AIOC, so that pdn-lin can open the card
-- directly: nothing between the modem and the converter, and no "device busy". The AIOC is only
-- ever a radio interface, so this cannot take anyone's headset away.
--
-- WirePlumber 0.4 only (Debian 12, Ubuntu 22.04 and 24.04); 0.5 reads the .conf the package
-- installs. The package does not put this where 0.4 looks, because 0.5 warns about anything there
-- at every start. On a 0.4 system, to let pdn-lin have the AIOC directly rather than through
-- PipeWire:
--
--   mkdir -p ~/.config/wireplumber/main.lua.d
--   cp /usr/share/doc/pdn-lin/examples/51-pdn-lin.lua ~/.config/wireplumber/main.lua.d/
--   systemctl --user restart wireplumber
table.insert(alsa_monitor.rules, {
  matches = {
    {
      { "device.vendor.id", "equals", "0x1209" },
      { "device.product.id", "equals", "0x7388" },
    },
  },
  apply_properties = {
    ["device.disabled"] = true,
  },
})
