﻿
/*
 * Gets top billboard albums and their ratings, builds a table row by row with the results. 
 */
function getBillboardResults() {
    $('#billboard-top-album').ready(function () {
        $.ajax({
            url: "/Scraper/ScrapeAlbumChart",
            method: "GET",
        }).done(function (response) {
            for (let i = 0; i < 100; i++) {
                getLinks(response.albums[i].artist, response.albums[i].title);
                $('#billboard-top-album > tbody:last-child').append('<tr><td>' + (i + 1) + '</td><td>' + response.albums[i].title + '</td><td>' + response.albums[i].artist + '</td></tr>');
            }
        })
    });
    
}

function allMusicScaperTest() {
    $.ajax({
        url: "/Scraper/AllMusicRatings",
        method: "GET",
        data: {
            musicBrainzId: 'mw0000096356',
            albumName: 'Kerplunk!'
        }
    }).done(function (response) {
        console.log(response);
    })
}

function getLinks(artist, album) {
    $.ajax({
        url: "/API/matchAlbum",
        method: "GET",
        data: {
            artistName: artist,
            albumName: album
        }
    }).done(function (response) {
        console.log(response);
    })
}
