-- pdn-lin: keep the desktop's sound server off the AIOC, so that pdn-lin can open the card
-- directly: nothing between the modem and the converter, and no "device busy". The AIOC is only
-- ever a radio interface, so this cannot take anyone's headset away.
--
-- WirePlumber 0.4 (Debian 12, Ubuntu 22.04 and 24.04). 0.5 reads the .conf beside this instead.
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
