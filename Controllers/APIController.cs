﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Albmer.Data;
using Albmer.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace Albmer.Controllers
{
    public class Billboard_Album
    {
       public string Title { get; set; }
       public string Artist { get; set; }
    }

    public class APIController : Controller
    {

        private static string userAgent = "Albmer/1.0.0a (https://www.utah.edu/)";
        private HttpClient client = new HttpClient();
        private readonly CacheContext _context;
        public APIController(CacheContext context) 
        {
            _context = context;
            client.DefaultRequestHeaders.Add("User-Agent", userAgent);
        }

        [HttpGet]
        public async Task<JsonResult> searchArtistAsync(string name)
        {
            List<Object> data = new List<Object>();
            var cachedResult = _context.Artists.Where(artist => artist.Name.ToLower().Equals(name.ToLower())).FirstOrDefault();
            if (cachedResult != null && cachedResult.Type != null) // Exists in cache and has detailed information
            {
                data.Add(cachedResult);
                return Json(new 
                { 
                    success = true, 
                    result = data
                });
            } 
            else
            {
                try
                {
                    HttpResponseMessage response = client.GetAsync("https://musicbrainz.org/ws/2/artist?query=" + name + "&fmt=json").Result;
                    response.EnsureSuccessStatusCode();
                    string responseBody = response.Content.ReadAsStringAsync().Result;
                    MusicBrainzArtistSearchResult result = JsonConvert.DeserializeObject<MusicBrainzArtistSearchResult>(responseBody);
                    if (result.artists.Count > 0)
                    {
                        foreach (MBArtist artist in result.artists) 
                        {
                            var dbArtist = new Artist { ID = artist.id, 
                                Name = artist.name, Type = artist.type, 
                                BeginYear = artist.life_span.begin, 
                                EndYear = artist.life_span.ended,
                                Origin = artist.begin_area != null ? artist.begin_area.name : null,
                                Genre = artist.tags != null ? tagsListToString(artist.tags) : null
                            };

                            /* Add or update block */
                            var cachedPoss = _context.Artists.Where(artist => artist.ID == dbArtist.ID).FirstOrDefault();
                            if (cachedPoss != null) 
                            {
                                _context.Remove(cachedPoss);
                                await _context.SaveChangesAsync();
                            }
                            await _context.Artists.AddAsync(dbArtist); // Cache results to db
                            /****/

                            data.Add(mbArtistToAnon(artist));
                        }
                        await _context.SaveChangesAsync();
                        return Json(new { success = true, result = data });
                    }
                    else
                        return Json(new { success = false, result = "No result found matching query" });
                }
                catch (HttpRequestException e)
                {
                    return Json(new { success = false, result = "Error: " + e });
                }
            }
        }

        private string tagsListToString(List<SubName> tags) 
        {
            if (tags == null) 
            {
                return null;
            }

            StringBuilder sb = new StringBuilder();
            foreach (SubName tag in tags) 
            {
                sb.Append(tag.name).Append(", ");
            }
 
            return sb.ToString()[0..^2];
        }

        // Used to parse MBArtist to pass off as json
        private Object mbArtistToAnon(MBArtist artist) 
        {
            return (new 
            { 
                artist.id,
                artist.name,
                origin = artist.begin_area != null ? artist.begin_area.name : null, 
                genre = artist.tags != null ? tagsListToString(artist.tags) : null, 
                begin_year = artist.life_span.begin, 
                end_year = artist.life_span.ended 
            });
        }

        // Parse Artist to formatted object for json
        private Object cachedArtistToAnon(Artist artist)
        {
            return (new
            { 
                id = artist.ID,
                name = artist.Name,
                origin = artist.Origin,
                begin_year = artist.BeginYear,
                end_year = artist.EndYear,
                genre = artist.Genre
            });
        }

        [HttpGet]
        public async Task<JsonResult> searchAlbumAsync(string name)
        {
            List<Object> data = new List<Object>();
            var cachedResult = _context.Albums.Where(album => album.Title.ToLower().Equals(name.ToLower())).FirstOrDefault();
            if (cachedResult != null) // Exists in cache
            {
                data.Add(cachedResult);
                return Json(new
                {
                    success = true,
                    result = data
                });
            }
            else 
            {
                try
                {
                    HttpResponseMessage response = client.GetAsync("http://musicbrainz.org/ws/2/release-group/?query=" + name + "%20AND%20type:album&fmt=json").Result;
                    response.EnsureSuccessStatusCode();
                    string responseBody = response.Content.ReadAsStringAsync().Result;
                    MusicBrainzAlbumSearchResult result = JsonConvert.DeserializeObject<MusicBrainzAlbumSearchResult>(responseBody);
                    if (result.release_groups.Count > 0)
                    {
                        // Cache
                        foreach (MBAlbum album in result.release_groups)
                        {
                            var dbAlbum = new Album
                            {
                                ID = album.id,
                                Title = album.title,
                                TrackCount = album.count,
                                Genre = tagsListToString(album.tags)
                            };

                            foreach (ArtistCredit credit in album.artist_credit)
                            {
                                // Add artist to cache if not in cache
                                Artist artist = _context.Artists.Where(artist => artist.ID == credit.artist.id).FirstOrDefault();
                                if (artist == null) 
                                {
                                    artist = new Artist { ID = credit.artist.id, Name = credit.artist.name };
                                    await _context.Artists.AddAsync(artist);
                                }

                                // Add relations to artist and album entities
                                ArtistAlbum rel = new ArtistAlbum 
                                { 
                                    Artist = artist,
                                    ArtistId = artist.ID,
                                    Album = dbAlbum,
                                    AlbumId = dbAlbum.ID
                                };
                                dbAlbum.ArtistAlbum.Add(rel);
                                artist.ArtistAlbum.Add(rel);

                                _context.Artists.Update(artist);
                            }

                            await _context.Albums.AddAsync(dbAlbum);
                            await _context.SaveChangesAsync();

                            data.Add(mbAlbumToAnon(album));
                        }
                        return Json(new { success = true, result = data });
                    }
                    else
                        return Json(new { success = false, result = "No result found matching query" });
                }
                catch (HttpRequestException e)
                {
                    return Json(new { success = false, result = "Error: " + e });
                }
            }
        }

        private Object mbAlbumToAnon(MBAlbum album)
        {
            return (new
            { 
                album.id,
                album.title,
                track_count = album.count,
                artists = album.artist_credit.Select(credit => new { credit.artist.name, credit.artist.id}),
                genre = tagsListToString(album.tags)
            });
        }

        [HttpGet]
        public async Task<JsonResult> artistDetailsAsync(string id)
        {
            var artist = _context.Artists.Where(artist => artist.ID.Equals(id))
                .Include(artist => artist.ArtistAlbum)
                .ThenInclude(aa => aa.Album)
                .FirstOrDefault();
            if (artist != null && isDetailedArtist(artist)) // Exists in cache
            {
                return Json(new
                {
                    success = true,
                    result = DetailedArtistToAnon(artist)
                });
            }
            else
            {
                try
                {
                    HttpResponseMessage response = client.GetAsync("https://musicbrainz.org/ws/2/artist/" + id + "?fmt=json&inc=url-rels+release-groups+artist-rels").Result;
                    response.EnsureSuccessStatusCode();
                    string responseBody = response.Content.ReadAsStringAsync().Result;
                    ArtistDetails result = JsonConvert.DeserializeObject<ArtistDetails>(responseBody);
                    if (result.error == null)
                    {
                        if (artist == null) 
                        {
                            artist = new Artist { ID = id, Name = result.name, Type = result.type};
                            _context.Artists.Add(artist);
                            _context.SaveChanges();
                        }

                        artist.Image = result.relations.Where(relation => relation.type.Equals("image"))
                            .Select(relation => relation.url.resource)
                            .FirstOrDefault();

                        if (artist.Type.ToLower().Equals("group"))
                        {
                            // TODO: Add band members to database
                            //var band_memebers = result.relations.Where(relation => relation.type.Equals("member of band"))
                            //                        .Select(relation => new
                            //                        {
                            //                            relation.artist.id,
                            //                            relation.artist.name,
                            //                            start_year = relation.begin,
                            //                            end_year = relation.end
                            //                        });
                        }

                        artist.OfficialWebsite = result.relations.Where(relation => relation.type.Equals("official homepage"))
                            .Select(relation => relation.url.resource)
                            .FirstOrDefault();

                        artist.AllMusic = result.relations.Where(relation => relation.type.Equals("allmusic"))
                            .Select(relation => relation.url.resource)
                            .FirstOrDefault();

                        artist.Discogs = result.relations.Where(relation => relation.type.Equals("discogs"))
                            .Select(relation => relation.url.resource)
                            .FirstOrDefault();

                        artist.RateYourMusic = result.relations.Where(relation => relation.type.Equals("other databases") && relation.url.resource.Contains("rateyourmusic.com"))
                            .Select(relation => relation.url.resource)
                            .FirstOrDefault();

                        // Add album relations
                        foreach (ReleaseGroup release in result.albums)
                        {
                            var album = _context.Albums.Where(album => album.ID == release.id).FirstOrDefault();
                            if (album == null)
                            {
                                album = new Album { ID = release.id, Title = release.title, Date = release.release_date};
                                _context.Add(album);
                                _context.SaveChanges();
                            }

                            // Only add relation if doesn't exist
                            bool exists = false;
                            foreach (ArtistAlbum relations in artist.ArtistAlbum)
                            {
                                if (relations.AlbumId.Equals(album.ID)) 
                                {
                                    exists = true;
                                    break;
                                }
                            }
                            if (!exists)
                            {
                                ArtistAlbum rel = new ArtistAlbum { Artist = artist, ArtistId = artist.ID, Album = album, AlbumId = album.ID };
                                artist.ArtistAlbum.Add(rel);
                                album.ArtistAlbum.Add(rel);

                                _context.Albums.Update(album); // Update album with new relations
                                _context.SaveChanges();
                            }
                        }

                        _context.Artists.Update(artist);
                        await _context.SaveChangesAsync();

                        return Json(new { success = true, result = DetailedArtistToAnon(artist) });
                    }
                    else
                        return Json(new { success = false, result = "No result found matching query" });
                }
                catch (HttpRequestException e)
                {
                    return Json(new { success = false, result = "Error: " + e });
                }
            }
        }

        private bool isDetailedArtist(Artist artist)
        {
            if(artist.OfficialWebsite != null || 
                artist.RateYourMusic != null ||
                artist.Discogs != null ||
                artist.AllMusic != null ||
                artist.Image != null)
            {
                return true;
            }
            return false;
        }

        private Object DetailedArtistToAnon(Artist artist) 
        {
            return (new
            { 
                name = artist.Name,
                image = artist.Image,
                begin_year = artist.BeginYear,
                end_year = artist.EndYear,
                origin = artist.Origin,
                type = artist.Type,
                albums = artist.ArtistAlbum.Select(m => new { m.AlbumId, m.Album.Title}),
                official_website = artist.OfficialWebsite,
                allmusic = artist.AllMusic,
                discogs = artist.Discogs,
                rate_your_music = artist.RateYourMusic
            });
        }

        //[HttpGet]
        //public async Task<JsonResult> matchAlbum(string artistName, string albumName)
        //{
        //    List<Object> data = new List<Object>();
        //    var cachedResults = _context.Albums.Where(album => album.Title.ToLower().Equals(albumName.ToLower()))
        //        .Select(album => album.ArtistAlbum).ToList();
        //    foreach (List<ArtistAlbum> rels in cachedResults)
        //    {
        //        foreach (ArtistAlbum rel in rels) 
        //        {
        //            if (rel.Artist.Name.ToLower().Equals(artistName.ToLower()))
        //                return artistDet;
        //        }
        //    }
        //    if (cachedResult != null) // Exists in cache
        //    {
        //        data.Add(cachedResult);
        //        return Json(new
        //        {
        //            success = true,
        //            result = data
        //        });
        //    }
        //    else
        //    {
        //        try
        //        {
        //            HttpResponseMessage response = client.GetAsync("http://musicbrainz.org/ws/2/release-group/?query=" + name + "%20AND%20type:album&fmt=json").Result;
        //            response.EnsureSuccessStatusCode();
        //            string responseBody = response.Content.ReadAsStringAsync().Result;
        //            MusicBrainzAlbumSearchResult result = JsonConvert.DeserializeObject<MusicBrainzAlbumSearchResult>(responseBody);
        //            if (result.release_groups.Count > 0)
        //            {
        //                // Cache
        //                foreach (MBAlbum album in result.release_groups)
        //                {
        //                    var dbAlbum = new Album
        //                    {
        //                        ID = album.id,
        //                        Title = album.title,
        //                        TrackCount = album.count,
        //                        Genre = tagsListToString(album.tags)
        //                    };

        //                    foreach (ArtistCredit credit in album.artist_credit)
        //                    {
        //                        // Add artist to cache if not in cache
        //                        var artistPoss = _context.Artists.Where(artist => artist.ID == credit.artist.id).FirstOrDefault();
        //                        Artist artist = null;
        //                        if (artistPoss == null)
        //                        {
        //                            artist = new Artist { ID = credit.artist.id, Name = credit.artist.name };
        //                            _context.Artists.Add(artist);
        //                        }
        //                        if (artist == null)
        //                        {
        //                            artist = _context.Artists.Where(artist => artist.ID == credit.artist.id).First();
        //                        }
        //                        // Add relations to artist and album entities
        //                        ArtistAlbum rel = new ArtistAlbum
        //                        {
        //                            Artist = artist,
        //                            Album = dbAlbum
        //                        };
        //                        dbAlbum.ArtistAlbum.Add(rel);

        //                        _context.Albums.Add(dbAlbum);
        //                    }

        //                    await _context.SaveChangesAsync();

        //                    data.Add(mbAlbumToAnon(album));
        //                }
        //                return Json(new { success = true, result = data });
        //            }
        //            else
        //                return Json(new { success = false, result = "No result found matching query" });
        //        }
        //        catch (HttpRequestException e)
        //        {
        //            return Json(new { success = false, result = "Error: " + e });
        //        }
        //    }
        //}
    }
}