#!/usr/bin/env node
// Run this LOCALLY to turn a chosen admin password into the value stored as
// the `ADMIN_PASSWORD_HASH` Worker secret. The plaintext password is never
// written to disk, never committed, and never sent anywhere by this script
// -- it only prints the resulting hash to your terminal for you to copy.
//
// Usage:
//   node scripts/hash-admin-password.mjs
//   (you will be prompted for the password; it is not echoed to the terminal)
//
// Then, when ready to deploy (requires your own explicit authorization,
// never done automatically by this scaffold):
//   wrangler secret put ADMIN_PASSWORD_HASH --env development
//   wrangler secret put ADMIN_PASSWORD_HASH --env production
//
// Also set a random, unrelated IP_HASH_SECRET the same way, e.g.:
//   node -e "console.log(crypto.randomUUID() + crypto.randomUUID())"
//   wrangler secret put IP_HASH_SECRET --env development
//   wrangler secret put IP_HASH_SECRET --env production

import { createInterface } from 'node:readline/promises';
import { stdin, stdout } from 'node:process';
import { hashPassword } from '../src/auth/crypto.js';

async function readPasswordHidden(prompt) {
  return new Promise((resolve) => {
    stdout.write(prompt);
    const onData = (chunk) => {
      const text = chunk.toString('utf8');
      if (text.includes('\n') || text.includes('\r')) {
        stdin.setRawMode?.(false);
        stdin.pause();
        stdin.removeListener('data', onData);
        stdout.write('\n');
        resolve(collected.replace(/[\r\n]+$/, ''));
      }
    };

    let collected = '';
    stdin.setRawMode?.(true);
    stdin.resume();
    stdin.on('data', (chunk) => {
      collected += chunk.toString('utf8');
    });
    stdin.on('data', onData);
  });
}

async function main() {
  const isInteractive = Boolean(stdin.isTTY);
  let password;

  if (isInteractive) {
    password = await readPasswordHidden('Admin password (input hidden): ');
  } else {
    const rl = createInterface({ input: stdin, output: stdout });
    password = await rl.question('Admin password: ');
    rl.close();
  }

  if (!password || password.length < 12) {
    console.error('\nRefusing to hash an empty or overly short (<12 characters) password.');
    process.exitCode = 1;
    return;
  }

  const hash = await hashPassword(password);
  console.log('\nADMIN_PASSWORD_HASH value (paste this into `wrangler secret put ADMIN_PASSWORD_HASH`):\n');
  console.log(hash);
}

main();
