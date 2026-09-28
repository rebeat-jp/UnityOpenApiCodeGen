#!/usr/bin/env node
'use strict';
// Personal activation follows GameCI unity-builder's getSerialFromLicenseFile:
// DeveloperData is base64, with four prefix bytes preceding the serial.
function serialFromLicense(license) {
  const match = /<DeveloperData\s+Value="([A-Za-z0-9+/=]+)"\s*\/>/.exec(license || '');
  if (!match) throw new Error('UNITY_LICENSE lacks valid DeveloperData; check the license Secret');
  const serial = Buffer.from(match[1], 'base64').toString('latin1').slice(4);
  if (!/^F[0-9A-Z]-(?:[0-9A-Z]{4}-){4}[0-9A-Z]{4}$/.test(serial)) throw new Error('UNITY_LICENSE is not a supported Personal license');
  return serial;
}
module.exports = { serialFromLicense };
if (require.main === module) {
  try { process.stdout.write(serialFromLicense(process.env.UNITY_LICENSE)); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
