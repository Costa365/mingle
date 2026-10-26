import { test as base, expect } from '@playwright/test'
import { spawn, execFile } from 'node:child_process'
import type { ChildProcess } from 'node:child_process'
import { once } from 'node:events'
import { mkdtemp, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { promisify } from 'node:util'

interface Backend {
  start: () => Promise<void>
  stop: () => Promise<void>
}
const repository = path.resolve(import.meta.dirname, '../..')
const exec = promisify(execFile)

export const test = base.extend<object, { backend: Backend }>({
  backend: [
    async ({}, use) => {
      const composeProject = process.env.TEAMGAMES_TEST_COMPOSE_PROJECT
      if (process.env.PLAYWRIGHT_BASE_URL && !composeProject) {
        throw new Error(
          'External browser tests require TEAMGAMES_TEST_COMPOSE_PROJECT for real API stop/restart checks.',
        )
      }
      const directory = await mkdtemp(path.join(tmpdir(), 'teamgames-browser-'))
      let processHandle: ChildProcess | undefined
      let output = ''
      const healthUrl = process.env.PLAYWRIGHT_BASE_URL
        ? `${process.env.PLAYWRIGHT_BASE_URL}/api/health/ready`
        : 'http://127.0.0.1:5101/api/health/ready'

      const backend: Backend = {
        async start() {
          if (composeProject) {
            await exec('docker', ['compose', '-p', composeProject, 'start', 'api'], {
              cwd: repository,
            })
          } else {
            processHandle = spawn(
              'dotnet',
              [path.join(repository, 'server/TeamGames.Api/bin/Debug/net10.0/TeamGames.Api.dll')],
              {
                cwd: path.join(repository, 'server/TeamGames.Api'),
                env: {
                  ...process.env,
                  ASPNETCORE_URLS: 'http://127.0.0.1:5101',
                  ASPNETCORE_ENVIRONMENT: 'Production',
                  Storage__DataDirectory: directory,
                  Security__AllowInsecureCookies: 'true',
                },
                stdio: ['ignore', 'pipe', 'pipe'],
              },
            )
            processHandle.stdout?.on('data', (data: Buffer) => {
              output = (output + data.toString()).slice(-8000)
            })
            processHandle.stderr?.on('data', (data: Buffer) => {
              output = (output + data.toString()).slice(-8000)
            })
          }
          await expect
            .poll(
              async () => {
                try {
                  return (await fetch(healthUrl, { signal: AbortSignal.timeout(1500) })).status
                } catch {
                  return 0
                }
              },
              // Container restart can require Docker DNS and proxy resolution to recover.
              {
                timeout: composeProject ? 60_000 : 25_000,
                message: `API must become ready. ${output}`,
              },
            )
            .toBe(200)
        },
        async stop() {
          if (composeProject) {
            await exec('docker', ['compose', '-p', composeProject, 'stop', 'api'], {
              cwd: repository,
            })
          } else if (processHandle && processHandle.exitCode === null) {
            const exited = once(processHandle, 'exit')
            processHandle.kill('SIGTERM')
            await exited
            processHandle = undefined
          }
        },
      }
      try {
        await backend.start()
        await use(backend)
      } finally {
        if (!composeProject) await backend.stop()
        await rm(directory, { recursive: true, force: true })
      }
    },
    { scope: 'worker', auto: true },
  ],
})

export { expect } from '@playwright/test'
