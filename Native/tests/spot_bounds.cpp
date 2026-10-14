#include "../src/spot_bounds.h"
#include <array>
#include <iostream>
#include <random>
#include <stdexcept>

void require(bool value,const char* message){if(!value)throw std::runtime_error(message);}
int main(){
    const float axis[]{0,0,1};float edge[]{20,0,10};
    // At 60 degrees the cone side-plane is only 1.34 units from this
    // centre, so a radius 2.5 sphere intersects it. The old horizontal
    // margin (10*sqrt(3)+2.5 = 19.82) incorrectly rejected side=20.
    require(spot_sphere_visible(edge,axis,40,.5f,2.5f),"cone-edge caster was rejected");
    float outside[]{30,0,10},behind[]{0,0,-10},far[]{0,0,100};
    require(!spot_sphere_visible(outside,axis,40,.5f,1),"outside cone was retained");
    require(!spot_sphere_visible(behind,axis,40,.5f,1),"behind apex was retained");
    require(!spot_sphere_visible(far,axis,40,.5f,1),"outside light range was retained");
    require(spot_sphere_visible(far,axis,40,.5f,-1),"unknown bound was culled");
    std::mt19937 random(0x5E2026);std::uniform_real_distribution<float> unit(0,1);
    for(int i=0;i<100000;++i){
        float cosine=.06f+.93f*unit(random),sine=std::sqrt(1-cosine*cosine);
        float z=1+30*unit(random),side=z*sine/cosine*(.8f+.199f*unit(random));
        float azimuth=unit(random)*6.2831853f;
        // This witness point is inside both the cone and range sphere.
        float point[]{side*std::cos(azimuth),side*std::sin(azimuth),z};
        float range=std::sqrt(side*side+z*z)+1;
        float offset[]{2*unit(random)-1,2*unit(random)-1,2*unit(random)-1};
        float radius=std::sqrt(offset[0]*offset[0]+offset[1]*offset[1]+offset[2]*offset[2])+.01f;
        float centre[]{point[0]+offset[0],point[1]+offset[1],point[2]+offset[2]};
        require(spot_sphere_visible(centre,axis,range,cosine,radius),"sphere containing a visible witness was culled");
        // An enclosing batch sphere also contains the same visible point.
        float shift[]{2*unit(random)-1,2*unit(random)-1,2*unit(random)-1};
        float parent[]{centre[0]+shift[0],centre[1]+shift[1],centre[2]+shift[2]};
        float parentRadius=radius+std::sqrt(shift[0]*shift[0]+shift[1]*shift[1]+shift[2]*shift[2])+.01f;
        require(spot_sphere_visible(parent,axis,range,cosine,parentRadius),"enclosing batch lost a visible caster");
    }
    std::cout<<"Spot bounds: edge/range/apex and 100000 visible witness/enclosing sphere cases passed\n";
}
