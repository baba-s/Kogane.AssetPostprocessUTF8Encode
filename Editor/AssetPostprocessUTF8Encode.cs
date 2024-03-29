﻿using System;
using System.IO;
using System.Text;
using UnityEditor;

namespace Kogane.Internal
{
    /// <summary>
    /// 新規スクリプト生成時にテキストエンコードをUTF-8に変換する
    /// </summary>
    internal sealed class AssetPostprocessUTF8Encode : AssetPostprocessor
    {
        private static readonly Encoding m_encoding = Encoding.GetEncoding( "utf-8" );

        private static string m_currentDirectory;

        private void OnPreprocessAsset()
        {
            var monoImporter = assetImporter as MonoImporter;

            if ( monoImporter == null ) return;

            m_currentDirectory ??= Directory.GetCurrentDirectory().Replace( "\\", "/" ) + "/";

            var fullPath     = Path.GetFullPath( assetPath ).Replace( "\\", "/" );
            var relativePath = fullPath.Replace( m_currentDirectory, "" );

            if ( !relativePath.StartsWith( "Assets/", StringComparison.Ordinal ) &&
                 !relativePath.StartsWith( "Packages/", StringComparison.Ordinal ) )
            {
                return;
            }

            if ( File.Exists( assetPath ) )
            {
                var ext = Path.GetExtension( assetPath );
                if ( ext == ".cs" || ext == ".js" || ext == ".boo" )
                {
                    // ファイルを開く
                    using var fs = new FileStream( assetPath, FileMode.Open, FileAccess.Read );
                    var       bs = new byte[ fs.Length ];
                    fs.Read( bs, 0, bs.Length );
                    fs.Close();

                    // エンコードを取得
                    var enc = GetCode( bs );

                    if ( enc != null )
                    {
                        // "utf-8"以外を処理する
                        if ( enc.CodePage == 65001 )
                        {
                            // BOMを確認
                            if ( ( bs[ 0 ] == 0xEF ) && ( bs[ 1 ] == 0xBB ) && ( bs[ 2 ] == 0xBF ) )
                            {
                                return;
                            }
                        }

                        // 改行コードの置き換え
                        var contents = enc.GetString( bs ).Replace( "\r\n", "\n" );

                        // ファイルを保存
                        File.WriteAllText( assetPath, contents, m_encoding );
                        // Debug.LogWarning( "convert script encode to UTF-8N : " + asset );
                    }
                }
            }
        }

        /// <summary>
        /// 文字コードを判別する
        /// </summary>
        /// <remarks>
        /// Jcode.pmのgetcodeメソッドを移植したものです。
        /// Jcode.pm(http://openlab.ring.gr.jp/Jcode/index-j.html)
        /// Jcode.pmのCopyright: Copyright 1999-2005 Dan Kogai
        /// </remarks>
        /// <param name="bytes">文字コードを調べるデータ</param>
        /// <returns>適当と思われるEncodingオブジェクト。
        /// 判断できなかった時はnull。</returns>
        private static Encoding GetCode( byte[] bytes )
        {
            const byte bEscape = 0x1B;
            const byte bAt     = 0x40;
            const byte bDollar = 0x24;
            const byte bAnd    = 0x26;
            const byte bOpen   = 0x28; // '('
            const byte bB      = 0x42;
            const byte bD      = 0x44;
            const byte bJ      = 0x4A;
            const byte bI      = 0x49;

            var  len = bytes.Length;
            byte b1, b2, b3, b4;

            // Encode::is_utf8 は無視

            var isBinary = false;
            for ( var i = 0; i < len; i++ )
            {
                b1 = bytes[ i ];

                if ( b1 <= 0x06 || b1 == 0x7F || b1 == 0xFF )
                {
                    // 'binary'
                    isBinary = true;
                    if ( b1 == 0x00 && i < len - 1 && bytes[ i + 1 ] <= 0x7F )
                    {
                        // smells like raw unicode
                        return Encoding.Unicode;
                    }
                }
            }

            if ( isBinary )
            {
                return null;
            }

            // not Japanese
            var notJapanese = true;
            for ( var i = 0; i < len; i++ )
            {
                b1 = bytes[ i ];

                if ( b1 == bEscape || 0x80 <= b1 )
                {
                    notJapanese = false;
                    break;
                }
            }

            if ( notJapanese )
            {
                return Encoding.ASCII;
            }

            for ( var i = 0; i < len - 2; i++ )
            {
                b1 = bytes[ i ];
                b2 = bytes[ i + 1 ];
                b3 = bytes[ i + 2 ];

                if ( b1 == bEscape )
                {
                    if ( b2 == bDollar && b3 == bAt )
                    {
                        // JIS_0208 1978
                        return Encoding.GetEncoding( 50220 );
                    }

                    if ( b2 == bDollar && b3 == bB )
                    {
                        // JIS_0208 1983
                        return Encoding.GetEncoding( 50220 );
                    }

                    if ( b2 == bOpen && ( b3 == bB || b3 == bJ ) )
                    {
                        // JIS_ASC
                        return Encoding.GetEncoding( 50220 );
                    }

                    if ( b2 == bOpen && b3 == bI )
                    {
                        // JIS_KANA
                        return Encoding.GetEncoding( 50220 );
                    }

                    if ( i < len - 3 )
                    {
                        b4 = bytes[ i + 3 ];

                        if ( b2 == bDollar && b3 == bOpen && b4 == bD )
                        {
                            //JIS_0212
                            return Encoding.GetEncoding( 50220 );
                        }

                        if ( i < len - 5 && b2 == bAnd && b3 == bAt && b4 == bEscape && bytes[ i + 4 ] == bDollar && bytes[ i + 5 ] == bB )
                        {
                            //JIS_0208 1990
                            return Encoding.GetEncoding( 50220 );
                        }
                    }
                }
            }

            // should be euc|sjis|utf8
            // use of (?:) by Hiroki Ohzaki <ohzaki@iod.ricoh.co.jp>
            var sjis = 0;
            var euc  = 0;
            var utf8 = 0;

            for ( var i = 0; i < len - 1; i++ )
            {
                b1 = bytes[ i ];
                b2 = bytes[ i + 1 ];

                if ( ( ( 0x81 <= b1 && b1 <= 0x9F ) || ( 0xE0 <= b1 && b1 <= 0xFC ) ) && ( ( 0x40 <= b2 && b2 <= 0x7E ) || ( 0x80 <= b2 && b2 <= 0xFC ) ) )
                {
                    // SJIS_C
                    sjis += 2;
                    i++;
                }
            }

            for ( var i = 0; i < len - 1; i++ )
            {
                b1 = bytes[ i ];
                b2 = bytes[ i + 1 ];

                if ( ( ( 0xA1 <= b1 && b1 <= 0xFE ) && ( 0xA1 <= b2 && b2 <= 0xFE ) ) || ( b1 == 0x8E && ( 0xA1 <= b2 && b2 <= 0xDF ) ) )
                {
                    // EUC_C
                    // EUC_KANA
                    euc += 2;
                    i++;
                }
                else if ( i < len - 2 )
                {
                    b3 = bytes[ i + 2 ];

                    if ( b1 == 0x8F && ( 0xA1 <= b2 && b2 <= 0xFE ) && ( 0xA1 <= b3 && b3 <= 0xFE ) )
                    {
                        // EUC_0212
                        euc += 3;
                        i   += 2;
                    }
                }
            }

            for ( var i = 0; i < len - 1; i++ )
            {
                b1 = bytes[ i ];
                b2 = bytes[ i + 1 ];

                if ( ( 0xC0 <= b1 && b1 <= 0xDF ) && ( 0x80 <= b2 && b2 <= 0xBF ) )
                {
                    // UTF8
                    utf8 += 2;
                    i++;
                }
                else if ( i < len - 2 )
                {
                    b3 = bytes[ i + 2 ];
                    if ( ( 0xE0 <= b1 && b1 <= 0xEF ) && ( 0x80 <= b2 && b2 <= 0xBF ) && ( 0x80 <= b3 && b3 <= 0xBF ) )
                    {
                        // UTF8
                        utf8 += 3;
                        i    += 2;
                    }
                }
            }

            // M. Takahashi's suggestion
            // utf8 += utf8 / 2;

            System.Diagnostics.Debug.WriteLine( "sjis = {0}, euc = {1}, utf8 = {2}", sjis, euc, utf8 );

            if ( euc > sjis && euc > utf8 )
            {
                // EUC
                return Encoding.GetEncoding( 51932 );
            }

            if ( sjis > euc && sjis > utf8 )
            {
                // SJIS
                return Encoding.GetEncoding( 932 );
            }

            if ( utf8 > euc && utf8 > sjis )
            {
                // UTF8
                return Encoding.UTF8;
            }

            return null;
        }
    }
}