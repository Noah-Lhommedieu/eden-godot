#!/usr/bin/env python3
"""Tests légers de pkgfs_unpack : archives synthétiques + index réel."""
import os
import struct
import sys
import tempfile
import unittest
import zlib

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))
import pkgfs_unpack as pu  # noqa: E402

REFORGED = os.environ.get(
    'REFORGED',
    '/home/ster2lune/Bureau/reverseden/eden_server/reforged/EdenReforged')
REAL_PKGDIR = os.path.join(REFORGED, 'pkg')
REAL_LIMIT = 200


def make_pkg(tmp: str, specs: list[dict]) -> str:
    """Écrit un pkg.idx + pkgNNN.pkg synthétiques dans ``tmp``.

    spec: dict(name=str, data=bytes, pkg=int=1, compress=bool=True,
               flags=int=0). Retourne le chemin de pkg.idx.
    """
    recs, blobs = [], {}
    for s in specs:
        data = s['data']
        pkg = s.get('pkg', 1)
        blob = blobs.setdefault(pkg, bytearray())
        off = len(blob)
        if s.get('compress', True) and not s.get('flags', 0) & 0x40:
            raw = zlib.compress(data)
            if len(raw) >= len(data):
                raw = data  # stocké brut : zsize == size
        else:
            raw = data
        blob += raw
        full = s['name']
        folder, _, fname = full.rpartition('/')
        folder = (folder + '/') if folder else ''
        rec = bytearray(pu.REC)
        struct.pack_into('<I', rec, 4, off)
        struct.pack_into('<I', rec, 12, len(raw))
        struct.pack_into('<Q', rec, 16, zlib.crc32(data))
        struct.pack_into('<Q', rec, 24, s.get('flags', 0))
        struct.pack_into('<I', rec, 56, len(data))
        rec[60:60 + len(fname)] = fname.encode('latin-1')
        rec[320:320 + len(folder)] = folder.encode('latin-1')
        struct.pack_into('<I', rec, 584, pkg)
        recs.append(rec)
    for pkg, blob in blobs.items():
        with open(os.path.join(tmp, 'pkg%03d.pkg' % pkg), 'wb') as f:
            f.write(blob)
    idx = os.path.join(tmp, 'pkg.idx')
    with open(idx, 'wb') as f:
        f.write(b'\0' * pu.HEADER)
        for r in recs:
            f.write(r)
    return idx


class TestRelPath(unittest.TestCase):
    def test_uidata_mapping(self):
        self.assertEqual(pu.rel_path('UIData\\layouts\\a.layout'),
                         'uidata/layouts/a.layout')
        self.assertEqual(pu.rel_path('UIDATA/x.png'), 'uidata/x.png')

    def test_separators(self):
        self.assertEqual(pu.rel_path('char\\texture\\A.dds'),
                         'char/texture/A.dds')
        self.assertEqual(pu.rel_path('\\data\\db\\i.ini'), 'data/db/i.ini')

    def test_traversal_rejected(self):
        for bad in ('../x', 'a/../../b', 'C:/evil', '/abs/../x', ''):
            with self.assertRaises(pu.UnsafePathError, msg=bad):
                pu.rel_path(bad)


class TestSyntheticPkg(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp(prefix='pkgfs-test-')
        self.specs = [
            dict(name='data/db/items.ini', data=b'V.2,5,a,b\n1,x\n' * 30),
            dict(name='UIData/layouts/hud.layout', data=b'<xml/>' * 100),
            dict(name='tex/a.dds', data=b'DDS ' + bytes(range(256)) * 8,
                 compress=False),
            dict(name='tex/b.dds', data=b'\x00' * 4096, pkg=2),
        ]
        make_pkg(self.tmp, self.specs)

    def tearDown(self):
        pu._proc_handles.clear()

    def _args(self, **kw):
        import argparse
        d = dict(pkgdir=self.tmp, limit=0, only=None, jobs=2,
                 against=None, out=None, force=False)
        d.update(kw)
        return argparse.Namespace(**d)

    def test_parse_and_verify(self):
        entries = pu.parse_index(os.path.join(self.tmp, 'pkg.idx'))
        self.assertEqual(len(entries), len(self.specs))
        self.assertEqual(entries[1].rel, 'uidata/layouts/hud.layout')
        self.assertEqual(pu.cmd_verify(self._args()), 0)

    def test_verify_against_ok_and_diff(self):
        out = os.path.join(self.tmp, 'out')
        self.assertEqual(pu.cmd_extract(self._args(out=out)), 0)
        self.assertEqual(pu.cmd_verify(self._args(against=out)), 0)
        # altère un fichier extrait -> verify doit le voir
        f = os.path.join(out, 'tex', 'a.dds')
        with open(f, 'ab') as w:
            w.write(b'x')
        self.assertEqual(pu.cmd_verify(self._args(against=out)), 1)

    def test_extract_skip_and_force(self):
        out = os.path.join(self.tmp, 'out')
        a = self._args(out=out)
        self.assertEqual(pu.cmd_extract(a), 0)
        dst = os.path.join(out, 'tex', 'b.dds')
        os.utime(dst, (0, 0))  # mtime repère
        self.assertEqual(pu.cmd_extract(a), 0)
        self.assertEqual(os.stat(dst).st_mtime, 0)  # skippé
        self.assertEqual(pu.cmd_extract(self._args(out=out, force=True)), 0)
        self.assertNotEqual(os.stat(dst).st_mtime, 0)

    def test_verify_detects_missing_pkg(self):
        entries = pu.parse_index(os.path.join(self.tmp, 'pkg.idx'))
        os.unlink(os.path.join(self.tmp, 'pkg002.pkg'))
        pu._proc_handles.clear()
        self.assertEqual(pu.cmd_verify(self._args()), 1)


class TestRealIndex(unittest.TestCase):
    @unittest.skipUnless(os.path.exists(os.path.join(REAL_PKGDIR, 'pkg.idx')),
                         'index réel absent')
    def test_first_n_entries(self):
        import argparse
        entries = pu.parse_index(os.path.join(REAL_PKGDIR, 'pkg.idx'))
        self.assertGreater(len(entries), 10000)
        a = argparse.Namespace(pkgdir=REAL_PKGDIR,
                               limit=REAL_LIMIT, only=None, jobs=4,
                               against=REFORGED)
        self.assertEqual(pu.cmd_verify(a), 0)


if __name__ == '__main__':
    unittest.main(verbosity=2)
