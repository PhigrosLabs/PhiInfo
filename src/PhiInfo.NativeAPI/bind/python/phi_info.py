import ctypes
import json
from typing import Optional, Any

class PhiInfo:
    def __init__(self, lib_path: str):
        self._lib = ctypes.CDLL(lib_path)
        self._initialized = False

        self._init_func = self._lib.init
        self._init_func.argtypes = [
            ctypes.c_void_p, ctypes.c_int,   # ggm
            ctypes.c_void_p, ctypes.c_int,   # level0
            ctypes.c_void_p, ctypes.c_int,   # level22
            ctypes.c_void_p, ctypes.c_int,   # cldb
            ctypes.c_void_p, ctypes.c_int,   # il2cppSo
            ctypes.c_void_p, ctypes.c_int    # metaData
        ]
        self._init_func.restype = ctypes.c_bool

        # reset()
        self._reset_func = self._lib.reset
        self._reset_func.argtypes = []
        self._reset_func.restype = ctypes.c_bool

        # extract_all()
        self._extract_all_func = self._lib.extract_all
        self._extract_all_func.argtypes = []
        self._extract_all_func.restype = ctypes.c_void_p

        # free_string(ptr)
        self._free_string_func = self._lib.free_string
        self._free_string_func.argtypes = [ctypes.c_void_p]
        self._free_string_func.restype = ctypes.c_bool

        # get_last_error()
        self._get_last_error_func = self._lib.get_last_error
        self._get_last_error_func.argtypes = []
        self._get_last_error_func.restype = ctypes.c_void_p

    def _check_error(self, result: bool, func_name: str) -> None:
        if not result:
            err_ptr = self._get_last_error_func()
            if err_ptr:
                try:
                    err_str = ctypes.string_at(err_ptr).decode('utf-8')
                    err_data = json.loads(err_str)
                    msg = f"{func_name} failed: {err_data.get('Error', 'unknown error')}"
                    if err_data.get('StackTrace'):
                        msg += f"\nStack trace: {err_data['StackTrace']}"
                finally:
                    self._free_string_func(err_ptr)
                raise RuntimeError(msg)
            else:
                raise RuntimeError(f"{func_name} failed with no error information")

    def _bytes_to_ptr(self, data: bytes):
        buf = (ctypes.c_char * len(data)).from_buffer_copy(data)
        ptr = ctypes.addressof(buf)
        return ptr, len(data), buf

    def init(
        self,
        ggm: bytes,
        level0: bytes,
        level22: bytes,
        cldb: bytes,
        il2cpp_so: bytes,
        metadata: bytes
    ) -> None:
        buffers = []
        args = []

        for data in (ggm, level0, level22, cldb, il2cpp_so, metadata):
            ptr, length, buf = self._bytes_to_ptr(data)
            args.extend([ptr, length])
            if buf is not None:
                buffers.append(buf)

        result = self._init_func(*args)
        self._check_error(result, "init")
        self._initialized = True

    def reset(self) -> None:
        if self._initialized:
            result = self._reset_func()
            self._check_error(result, "reset")
            self._initialized = False

    def extract_all(self) -> dict[str, Any]:
        if not self._initialized:
            raise RuntimeError("PhiInfo not initialized. Call init() first.")

        ptr = self._extract_all_func()
        if ptr is None:
            self._check_error(False, "extract_all")

        try:
            json_bytes = ctypes.string_at(ptr)
            json_str = json_bytes.decode('utf-8')
            return json.loads(json_str)
        finally:
            self._free_string_func(ptr)

    def get_last_error(self) -> Optional[dict[str, str]]:
        ptr = self._get_last_error_func()
        if ptr:
            try:
                json_bytes = ctypes.string_at(ptr)
                json_str = json_bytes.decode('utf-8')
                return json.loads(json_str)
            finally:
                self._free_string_func(ptr)
        return None

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        self.reset()

    def __del__(self):
        self.reset()