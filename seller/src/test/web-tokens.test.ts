import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { SRC_ROOT } from './scan'
import { web } from '../theme'

// G-VIS (docs/00 #208): the seller centre and the admin reuse the buyer site's tokens — same value, same name
describe('web tokens', () => {
  const css = readFileSync(join(SRC_ROOT, '..', '..', 'web', 'src', 'index.css'), 'utf8')

  it('every mirrored token equals its value in web/src/index.css', () => {
    for (const [name, value] of Object.entries(web)) {
      const m = new RegExp(`^\\s*${name}:\\s*([^;]+);`, 'm').exec(css)
      expect(m?.[1].trim(), name).toBe(value)
    }
  })
})
